// The R16 harness entry point.
//
//   deno run -A tools/bench/run.ts             measure and report, comparing for information
//   deno run -A tools/bench/run.ts --check     the same, but a regression exits non-zero
//   deno run -A tools/bench/run.ts --baseline  rewrite baseline.json from this measurement
//
// Not a CI job, on purpose. This machine's own run-to-run spread is 16.3%, which is why the
// tolerances are 20% and 25%; a GitHub runner's is wider still, so every threshold would be
// noise and the job would be muted inside a month.
// This is the thing a person runs before and after an upgrade, on one machine, which is what
// spec 012 R16 was asking for and could not have.

import { dirname, fromFileUrl, join } from "@std/path";

import { drive } from "./load.ts";
import { startRssSampler } from "./rss.ts";
import { DEFAULT_PORT, findPublishedExe, publish, startApp, waitForHealthz } from "./server.ts";
import {
  type Baseline,
  comparable,
  compare,
  type HarnessSettings,
  type Machine,
  mb,
  type ScenarioMeasurement,
  summarize,
} from "./stats.ts";

export const SCENARIOS: Record<string, string> = {
  // The floor: routed, but renders nothing and touches no database. Section 14's
  // "C# JSON API direct" column served the same purpose.
  healthz: "/healthz",
  // The full page render -- Layout, CSP nonce, antiforgery mint. Section 14's
  // "HTMX + ASP.NET Core -- GET /" row, and the one an upgrade could move.
  home: "/",
};

export interface Options {
  check: boolean;
  writeBaseline: boolean;
  skipPublish: boolean;
  force: boolean;
  port: number;
  harness: HarnessSettings;
}

export const DEFAULTS: Options = {
  check: false,
  writeBaseline: false,
  skipPublish: false,
  force: false,
  port: DEFAULT_PORT,
  // 16 workers, 10 seconds, 3 runs -- section 14's own "three runs per test" discipline.
  harness: { concurrency: 16, durationMs: 10_000, runs: 3 },
};

export function parseArgs(args: string[]): Options {
  const options: Options = { ...DEFAULTS, harness: { ...DEFAULTS.harness } };

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    const value = (max = Number.MAX_SAFE_INTEGER) => {
      const next = args[++i];
      if (next === undefined) throw new Error(`${arg} needs a value.`);
      const parsed = Number(next);
      // Integer, not merely finite: `--runs 2.5` would loop twice and report three, and
      // `--port 70000` would surface as a sixty-second healthz timeout rather than an error.
      if (!Number.isInteger(parsed) || parsed <= 0 || parsed > max) {
        throw new Error(`${arg} needs a whole number between 1 and ${max}, got '${next}'.`);
      }
      return parsed;
    };

    switch (arg) {
      case "--check":
        options.check = true;
        break;
      case "--baseline":
        options.writeBaseline = true;
        break;
      case "--skip-publish":
        options.skipPublish = true;
        break;
      case "--force":
        options.force = true;
        break;
      case "--port":
        options.port = value(65_535);
        break;
      case "--duration":
        options.harness.durationMs = value(3600) * 1000;
        break;
      case "--runs":
        options.harness.runs = value(100);
        break;
      case "--concurrency":
        options.harness.concurrency = value(4096);
        break;
      default:
        throw new Error(`Unknown argument '${arg}'.`);
    }
  }

  if (options.check && options.writeBaseline) {
    throw new Error("--check and --baseline are opposites: one reads the baseline, the other overwrites it.");
  }

  return options;
}

export function fingerprint(): Machine {
  return {
    hostname: Deno.hostname(),
    cpus: navigator.hardwareConcurrency,
    os: Deno.build.os,
    arch: Deno.build.arch,
  };
}

export function repoRoot(from: string = dirname(fromFileUrl(import.meta.url))): string {
  let dir = from;
  while (true) {
    try {
      Deno.statSync(join(dir, "NativeWeb.slnx"));
      return dir;
    } catch {
      const parent = dirname(dir);
      if (parent === dir) throw new Error(`No NativeWeb.slnx above ${from}.`);
      dir = parent;
    }
  }
}

export function baselinePath(root: string): string {
  return join(root, "tools", "bench", "baseline.json");
}

async function measure(options: Options): Promise<Record<string, ScenarioMeasurement>> {
  const root = repoRoot();

  if (!options.skipPublish) {
    console.log("Publishing apps/web (Release)...");
    await publish(root);
  }

  const exePath = await findPublishedExe(root);
  console.log(`Binary: ${exePath}`);

  const app = await startApp({ repoRoot: root, port: options.port, exePath });
  const measurements: Record<string, ScenarioMeasurement> = {};

  // Everything after the spawn is inside the try, the client included: a throw between the two
  // would otherwise orphan the app on the port, and the next run would die waiting for a
  // /healthz that a stale process is already answering.
  let client: Deno.HttpClient | undefined;
  try {
    // The app's listener presents a cert this run just minted, so the driver has to trust that
    // one specifically. Not a global "ignore certificate errors": the harness talks to loopback
    // and to nothing else, and a client that accepts anything is a habit worth not forming.
    client = Deno.createHttpClient({ caCerts: [app.cert.pem] });
    const trusting = client;
    const fetchImpl: typeof fetch = (input, init) =>
      fetch(input as string, { ...init, client: trusting } as unknown as RequestInit);

    let readyMs: number;
    try {
      readyMs = await waitForHealthz(
        app.baseUrl,
        60_000,
        fetchImpl,
        undefined,
        undefined,
        app.exitCode,
      );
    } catch (error) {
      // A port already in use, an unreadable cert, a startup guard refusing to boot: all of
      // them look identical from out here, and all of them say so in the app's own log.
      const tail = app.logTail();
      throw new Error(
        `${error instanceof Error ? error.message : String(error)}\n`
          + (tail.length > 0 ? `Its last output was:\n${tail}` : "It logged nothing at all."),
      );
    }
    console.log(`Up in ${Math.round(readyMs)}ms on ${app.baseUrl} (pid ${app.pid})\n`);

    for (const [name, path] of Object.entries(SCENARIOS)) {
      const url = `${app.baseUrl}${path}`;
      const { concurrency, durationMs, runs } = options.harness;

      // One discarded warm-up pass. Section 14 caught V8 climbing 1,291 -> 1,793 req/s across
      // three back-to-back runs; NativeAOT has no equivalent curve, but the OS page cache and
      // the socket pool do, and a cold first pass would drag the average down by more than
      // the band this harness is trying to detect.
      console.log(`${name}: warming up...`);
      await drive({ url, concurrency, durationMs: Math.min(durationMs, 3000), fetchImpl });

      const sampler = startRssSampler(app.pid, 250);
      const samples = [];
      try {
        for (let run = 1; run <= runs; run++) {
          const sample = await drive({ url, concurrency, durationMs, fetchImpl });
          const rps = sample.elapsedMs > 0
            ? Math.round((sample.requests / sample.elapsedMs) * 1000)
            : 0;
          console.log(`${name}: run ${run}/${runs} -- ${rps} req/s, ${sample.errors} errors`);
          samples.push(sample);
        }
      } finally {
        // Stopped even when a pass throws: otherwise the sampler goes on spawning tasklist
        // four times a second against a pid that is about to be killed.
        measurements[name] = summarize(samples, await sampler.stop());
      }

      console.log("");
    }
  } finally {
    client?.close();
    await app.stop();
  }

  return measurements;
}

function report(measurements: Record<string, ScenarioMeasurement>): void {
  console.log("scenario     req/s      p50      p99   errors   peak RSS");
  for (const [name, m] of Object.entries(measurements)) {
    console.log(
      `${name.padEnd(10)} ${String(m.rps).padStart(8)} ${`${m.p50Ms}ms`.padStart(8)} `
        + `${`${m.p99Ms}ms`.padStart(8)} ${String(m.errors).padStart(8)} `
        + `${`${mb(m.maxRssBytes)} MB`.padStart(10)}`,
    );
  }
  console.log("");
}

/**
 * Why this measurement must not become a baseline, or null if it may.
 *
 * A zero RSS means every sample failed, not that the server used no memory, and the first real
 * run of this harness recorded a baseline in which every GET / answered 500 -- 0 req/s and
 * 245,106 errors, written to the file without complaint. A baseline of a broken server is
 * worse than none: the floor under it is unfailable and the check reads green forever.
 */
export function baselineObjection(
  measurements: Record<string, ScenarioMeasurement>,
): string | null {
  const entries = Object.entries(measurements);
  if (entries.length === 0) return "nothing was measured";

  const failing = entries.filter(([, m]) => m.errors > 0);
  if (failing.length > 0) {
    return failing.map(([name, m]) => `${name} had ${m.errors} failed requests`).join(", ");
  }

  const idle = entries.filter(([, m]) => m.rps <= 0);
  if (idle.length > 0) {
    return `${idle.map(([name]) => name).join(", ")} served no requests`;
  }

  const unmeasured = entries.filter(([, m]) => m.maxRssBytes === 0);
  if (unmeasured.length > 0) {
    return `no RSS reading succeeded for ${unmeasured.map(([name]) => name).join(", ")}`;
  }

  return null;
}

/**
 * Why this measurement must not replace the committed baseline, or null if it may.
 *
 * The read side treats a machine mismatch as record-only and exits 0. Without the same rule on
 * the write side, a `--baseline` run on any other machine replaces the committed file, and every
 * later `--check` on the recording machine passes having compared nothing -- the gate does not
 * break, it goes quiet. `--force` is the deliberate re-baseline after a hardware change.
 */
export function overwriteObjection(
  existing: Baseline | null,
  machine: Machine,
  force: boolean,
): string | null {
  if (existing === null || force) return null;

  const before = existing.machine;
  if (
    before.hostname === machine.hostname && before.cpus === machine.cpus
    && before.os === machine.os && before.arch === machine.arch
  ) {
    return null;
  }

  return `the committed baseline was recorded on ${before.hostname} (${before.cpus} cpu, `
    + `${before.os}/${before.arch}) and this is ${machine.hostname} (${machine.cpus} cpu, `
    + `${machine.os}/${machine.arch}). Pass --force to replace it anyway.`;
}

/**
 * Read the baseline. Null means there is none; a malformed or half-written one throws rather
 * than being reported as absent, which would invite overwriting a file that exists.
 */
export async function readBaseline(path: string): Promise<Baseline | null> {
  let text: string;
  try {
    text = await Deno.readTextFile(path);
  } catch (error) {
    if (error instanceof Deno.errors.NotFound) return null;
    throw error;
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (error) {
    throw new Error(`${path} is not valid JSON: ${error instanceof Error ? error.message : error}`);
  }

  if (!isBaseline(parsed)) {
    throw new Error(
      `${path} is missing fields a baseline needs (recorded, machine, harness, scenarios). `
        + `Re-record it with --baseline.`,
    );
  }

  return parsed;
}

/**
 * Shape check, so a hand-edited baseline fails by name instead of as a TypeError in compare.
 *
 * The scenario VALUES are checked, not only the envelope. `"rps": "6998"` or `"rps": null`
 * parses as valid JSON and passes an envelope-only check, and then `base.rps * rpsFloor` is
 * NaN, and `now.rps < NaN` is false for every input there has ever been. The rps and RSS gates
 * would be unfailable from then on, with the file still round-tripping as a baseline and
 * nothing in the output saying so. A finite number is the requirement; NaN and Infinity are
 * numbers to `typeof` and would leave the same hole.
 */
export function isBaseline(value: unknown): value is Baseline {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Partial<Baseline>;

  const enveloped = typeof candidate.recorded === "string"
    && typeof candidate.machine?.hostname === "string"
    && typeof candidate.machine?.cpus === "number"
    && typeof candidate.harness?.concurrency === "number"
    && typeof candidate.harness?.durationMs === "number"
    && typeof candidate.harness?.runs === "number"
    && typeof candidate.scenarios === "object"
    && candidate.scenarios !== null;

  if (!enveloped) return false;

  return Object.values(candidate.scenarios as Record<string, unknown>).every(isMeasurement);
}

function isMeasurement(value: unknown): boolean {
  if (typeof value !== "object" || value === null) return false;
  const m = value as Record<string, unknown>;

  return ["rps", "p50Ms", "p99Ms", "errors", "maxRssBytes"]
    .every((field) => typeof m[field] === "number" && Number.isFinite(m[field]));
}

export interface Decision {
  code: number;
  message: string;
  /** Printed to stderr rather than stdout. */
  failed: boolean;
}

/**
 * What a measurement means against a baseline, as a value rather than as control flow, so the
 * exit codes are testable without publishing a binary and running a load test.
 */
export function decide(
  options: Options,
  baseline: Baseline | null,
  machine: Machine,
  measurements: Record<string, ScenarioMeasurement>,
): Decision {
  if (baseline === null) {
    return {
      // --check with nothing to check against is not a pass: a green exit there would mean
      // "measured and fine" while nothing was compared at all.
      code: options.check ? 2 : 0,
      message: "No baseline.json yet -- run with --baseline to record this measurement as one.",
      failed: options.check,
    };
  }

  const fit = comparable(baseline, machine, options.harness);
  if (!fit.ok) {
    // A settings mismatch is the operator's own flag, not a fact about the hardware, so under
    // --check it gets the same exit 2 as a missing baseline for the same reason: green would
    // read as "measured and fine" while nothing was compared. `--check --duration 5` used to
    // exit 0 on this path, which made the whole check optional for anyone who habitually
    // shortens a run.
    const unchecked = options.check && fit.kind === "settings";

    return {
      code: unchecked ? 2 : 0,
      message: unchecked
        ? `Nothing was compared: ${fit.reason}. Re-run with the baseline's settings, or drop `
          + `--check to record this measurement.`
        : `Record-only: ${fit.reason}. Nothing to compare against.`,
      failed: unchecked,
    };
  }

  const regressions = compare(baseline.scenarios, measurements);
  if (regressions.length === 0) {
    return {
      code: 0,
      message: `Within tolerance of the baseline recorded ${baseline.recorded}.`,
      failed: false,
    };
  }

  return {
    code: options.check ? 1 : 0,
    message: regressions
      .map((r) => `REGRESSION ${r.scenario} (${r.metric}): ${r.detail}`)
      .join("\n"),
    failed: true,
  };
}

export async function main(args: string[]): Promise<number> {
  const options = parseArgs(args);
  const root = repoRoot();
  const machine = fingerprint();

  const measurements = await measure(options);
  report(measurements);

  const scratch = join(root, "tmp", "bench");
  await Deno.mkdir(scratch, { recursive: true });
  const record = {
    _comment: "Spec 012 R16. Recorded by tools/bench/run.ts --baseline; compared by --check on "
      + "the same machine and the same harness settings, 20% down on req/s and 25% up on RSS. "
      + "Not comparable to the section 14 figures in docs/01-architecture -- different apps, "
      + "different tool, hardware that no longer exists. See tools/bench/README.md.",
    recorded: new Date().toISOString().slice(0, 10),
    machine,
    harness: options.harness,
    scenarios: measurements,
  } satisfies Baseline & { _comment: string };
  await Deno.writeTextFile(join(scratch, "latest.json"), `${JSON.stringify(record, null, 2)}\n`);
  console.log(`Wrote ${join(scratch, "latest.json")}`);

  if (options.writeBaseline) {
    const objection = baselineObjection(measurements)
      ?? overwriteObjection(await readBaseline(baselinePath(root)), machine, options.force);
    if (objection !== null) {
      console.error(`Refusing to record a baseline: ${objection}`);
      return 2;
    }

    await Deno.writeTextFile(baselinePath(root), `${JSON.stringify(record, null, 2)}
`);
    console.log(`Recorded ${baselinePath(root)}`);
    return 0;
  }

  const decision = decide(options, await readBaseline(baselinePath(root)), machine, measurements);
  if (decision.failed) console.error(decision.message);
  else console.log(decision.message);
  return decision.code;
}

if (import.meta.main) {
  try {
    Deno.exit(await main(Deno.args));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    Deno.exit(2);
  }
}
