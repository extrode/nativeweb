#!/usr/bin/env -S deno run -A
//
// Mutation triage: run Stryker.NET over the targets below and compare each score
// against the committed baseline.
//
//   deno run -A tools/mutation-triage.ts              # run all, compare, exit 1 on a drop
//   deno run -A tools/mutation-triage.ts --target scaffold
//   deno run -A tools/mutation-triage.ts --record     # rewrite the baseline from this run
//
// Why this exists rather than a coverage gate. Every line of the spec 017 paging seam
// was executed by tests and eight defects still shipped; what the suite lacked was
// assertions, not coverage. A mutation score measures that directly -- it asks whether
// changing the code makes a test fail -- so it is the only figure here worth gating on.
//
// Not a CI job. Stryker rebuilds and re-runs the suite once per surviving mutant, which
// is minutes per project, and the score moves with unrelated refactors. Run it when
// touching one of the targets, and record the new figure in the same branch.
//
// sample-integration is the least stable of the three and the tolerance below is what
// absorbs it: its mutants run a real HTTP host against real PostgreSQL, so how many time
// out depends on what else the machine is doing. Measured 2026-09-01, same commit, two
// runs: 17 timeouts and 39. Timeouts score as killed, so the figure drifts up under load.
// Read a change in that target as a signal to look, not as a fact on its own.
//
// That target also LEAVES ROWS BEHIND. Stryker kills a test host it judges hung, so the killed
// process never reaches the `finally` that removes its fixture rows, and the feed tests date
// theirs five years into the future on purpose. Measured 2026-09-01: a run ending 15:57 left
// five future-dated published rows in nativeweb_sample_test, and an e2e feed test failed on
// them an hour later. Since spec 019 the residue is self-clearing: both tiers' feed tests sweep
// feed-prefixed, future-dated rows minted more than thirty minutes earlier before they insert
// (SampleTestDatabase.SweepLeakedFeedRowsAsync and its e2e twin), so the next run of either
// suite removes what this one left. scripts/cleanup/018-paging-property-tests.ps1 remains the
// operator's fallback for looking without running a suite:
//
//   psql -d nativeweb_sample_test -U nativeweb_app //     -c "SELECT id, created_at FROM articles WHERE created_at > now();"
//   pwsh scripts/cleanup/018-paging-property-tests.ps1        # dry run; -e to act
//
// Every target runs under tools/stryker-config.json, passed with --config-file: ConfigureAwait
// mutants are ignored there because flipping the flag is behaviour-identical under a test host
// (about fifteen of the SQL store's survivors were that, on 2026-09-01), and the per-mutant
// additional timeout is 30 s rather than Stryker's 5 s so a host slow under load is not killed
// for it. The baseline was re-recorded under that config on 2026-09-02; a figure from before it
// is not comparable.
//
// Requires the global tool: dotnet tool install -g dotnet-stryker

interface Target {
	readonly name: string;
	readonly testProject: string;
	readonly underTest: string;
	readonly note: string;
}

const TARGETS: readonly Target[] = [
	{
		name: "scaffold",
		testProject: "tests/nativeweb.scaffold.tests.unit",
		underTest: "nativeweb.scaffold.csproj",
		note: "cursor codec, sort parsing, the admin index's read check",
	},
	{
		name: "sample-unit",
		testProject: "tests/nativeweb.sample.tests.unit",
		underTest: "nativeweb.sample.csproj",
		note: "the reference store's paging; Program.cs is out of this tier's reach",
	},
	{
		name: "sample-integration",
		testProject: "tests/nativeweb.scaffold.tests.integration",
		underTest: "nativeweb.sample.csproj",
		note: "the SQL store and the endpoints, against real PostgreSQL",
	},
];

const BASELINE = "tools/mutation-baseline.json";
const CONFIG = "tools/stryker-config.json";
const TOLERANCE = 1.0;

export interface Score {
	readonly score: number;
	readonly killed: number;
	readonly survived: number;
	readonly noCoverage: number;
	readonly timeout: number;
}

export interface MutationReport {
	readonly files: Record<string, { readonly mutants: { readonly status: string }[] }>;
}

type Baseline = Record<string, Score & { readonly recorded: string; readonly note: string }>;

// Timeout counts with Killed: a mutant that hangs the suite was detected by it. Stryker
// scores it the same way, and the two figures have to agree or the baseline is comparing
// against a number nobody can reproduce from the HTML report.
export function scoreReport(parsed: MutationReport): Score {
	const counts: Record<string, number> = {};

	for (const file of Object.values(parsed.files)) {
		for (const mutant of file.mutants) {
			counts[mutant.status] = (counts[mutant.status] ?? 0) + 1;
		}
	}

	const killed = counts["Killed"] ?? 0;
	const survived = counts["Survived"] ?? 0;
	const noCoverage = counts["NoCoverage"] ?? 0;
	const timeout = counts["Timeout"] ?? 0;
	const denominator = killed + survived + noCoverage + timeout;

	return {
		score: denominator === 0 ? 0 : (100 * (killed + timeout)) / denominator,
		killed,
		survived,
		noCoverage,
		timeout,
	};
}

// A drop beyond the tolerance is a regression; a rise never is. One-sided, because the
// score moves a little with any refactor and only the downward move is a claim about
// the tests getting weaker.
export function isRegression(before: number | undefined, after: number, tolerance: number): boolean {
	return before !== undefined && after - before < -tolerance;
}

function scoreOf(report: string): Score {
	return scoreReport(JSON.parse(Deno.readTextFileSync(report)) as MutationReport);
}

// Absolute paths throughout, because Stryker resolves --output and --config-file against
// its own working directory, which is the test project, not the repository.
export function strykerArgs(target: Pick<Target, "name" | "underTest">, root: string): string[] {
	return [
		"stryker",
		"--project",
		target.underTest,
		"--config-file",
		`${root}/${CONFIG}`,
		"--output",
		`${root}/tmp/stryker/${target.name}`,
		"--reporter",
		"json",
	];
}

async function run(target: Target): Promise<Score> {
	const command = new Deno.Command("dotnet", {
		args: strykerArgs(target, Deno.cwd()),
		cwd: `${Deno.cwd()}/${target.testProject}`,
		stdout: "inherit",
		stderr: "inherit",
	});

	const { code } = await command.output();

	if (code !== 0) {
		throw new Error(`stryker exited ${code} for ${target.name}`);
	}

	return scoreOf(`tmp/stryker/${target.name}/reports/mutation-report.json`);
}

if (import.meta.main) {
	const args = Deno.args;
	const record = args.includes("--record");
	const only = args.includes("--target") ? args[args.indexOf("--target") + 1] : null;
	const selected = only ? TARGETS.filter((t) => t.name === only) : TARGETS;

	if (selected.length === 0) {
		console.error(`unknown target '${only}'. known: ${TARGETS.map((t) => t.name).join(", ")}`);
		Deno.exit(2);
	}

	let baseline: Baseline = {};

	try {
		baseline = JSON.parse(Deno.readTextFileSync(BASELINE)) as Baseline;
	} catch {
		console.log(`no baseline at ${BASELINE}; this run will record one`);
	}

	const measured: Baseline = { ...baseline };
	let regressed = false;

	for (const target of selected) {
		const score = await run(target);
		const was = baseline[target.name]?.score;
		const delta = was === undefined ? 0 : score.score - was;
		const arrow = was === undefined ? "new" : delta >= 0 ? `+${delta.toFixed(2)}` : delta.toFixed(2);

		console.log(
			`${target.name.padEnd(20)} ${score.score.toFixed(2).padStart(6)}%  ${arrow.padStart(7)}  ` +
				`killed ${score.killed}, survived ${score.survived}, uncovered ${score.noCoverage}`,
		);

		if (isRegression(was, score.score, TOLERANCE)) {
			console.error(`  REGRESSION: ${target.name} fell ${(-delta).toFixed(2)} points below ${was!.toFixed(2)}%`);
			regressed = true;
		}

		measured[target.name] = {
			...score,
			recorded: new Date().toISOString().slice(0, 10),
			note: target.note,
		};
	}

	if (record) {
		Deno.writeTextFileSync(BASELINE, `${JSON.stringify(measured, null, "\t")}\n`);
		console.log(`recorded ${BASELINE}`);
	}

	Deno.exit(regressed ? 1 : 0);
}
