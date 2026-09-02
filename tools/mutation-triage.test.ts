import { assert, assertAlmostEquals, assertEquals, assertFalse } from "jsr:@std/assert@1";

import { isRegression, type MutationReport, scoreReport, strykerArgs } from "./mutation-triage.ts";

function report(...statuses: string[][]): MutationReport {
	return {
		files: Object.fromEntries(
			statuses.map((file, i) => [`f${i}.cs`, { mutants: file.map((status) => ({ status })) }]),
		),
	};
}

Deno.test("an empty report scores zero rather than dividing by zero", () => {
	assertEquals(scoreReport(report()).score, 0);
	assertEquals(scoreReport(report([])).score, 0);
});

Deno.test("a report where every mutant died scores 100", () => {
	assertEquals(scoreReport(report(["Killed", "Killed"])).score, 100);
});

Deno.test("a timeout counts as detected", () => {
	const scored = scoreReport(report(["Timeout", "Survived"]));

	assertEquals(scored.score, 50);
	assertEquals(scored.timeout, 1);
});

Deno.test("an uncovered mutant is counted against the score", () => {
	assertEquals(scoreReport(report(["Killed", "NoCoverage"])).score, 50);
});

Deno.test("compile errors and ignored mutants are outside the denominator", () => {
	assertEquals(scoreReport(report(["Killed", "CompileError", "Ignored"])).score, 100);
});

Deno.test("counts are summed across files", () => {
	const scored = scoreReport(report(["Killed", "Survived"], ["Killed", "Killed"]));

	assertEquals(scored.killed, 3);
	assertEquals(scored.survived, 1);
	assertAlmostEquals(scored.score, 75);
});

Deno.test("the scaffold baseline figure reproduces from its own counts", () => {
	assertAlmostEquals(
		scoreReport(report([
			...Array(123).fill("Killed"),
			...Array(10).fill("Survived"),
			...Array(30).fill("NoCoverage"),
		])).score,
		75.46,
		0.01,
	);
});

Deno.test("a first run with no recorded figure is never a regression", () => {
	assertFalse(isRegression(undefined, 0, 1));
});

Deno.test("a rise is never a regression", () => {
	assertFalse(isRegression(75, 90, 1));
});

Deno.test("a drop within the tolerance is not a regression", () => {
	assertFalse(isRegression(75, 74.5, 1));
	assertFalse(isRegression(75, 74, 1));
});

Deno.test("a drop past the tolerance is a regression", () => {
	assert(isRegression(75, 73.9, 1));
});

Deno.test("importing the script does not shell out to stryker", async () => {
	const module = await import("./mutation-triage.ts");

	assertEquals(typeof module.scoreReport, "function");
});

Deno.test("every target runs under the committed stryker config", () => {
	const args = strykerArgs({ name: "scaffold", underTest: "nativeweb.scaffold.csproj" }, "/repo");

	assertEquals(args[args.indexOf("--config-file") + 1], "/repo/tools/stryker-config.json");
	assertEquals(args[args.indexOf("--project") + 1], "nativeweb.scaffold.csproj");
	assertEquals(args[args.indexOf("--output") + 1], "/repo/tmp/stryker/scaffold");
});
