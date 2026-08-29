// The R16 harness entry point.
//
//   deno run -A tools/bench/run.ts             measure and report, comparing for information
//   deno run -A tools/bench/run.ts --check     the same, but a regression exits non-zero
//   deno run -A tools/bench/run.ts --baseline  rewrite baseline.json from this measurement
//
// Not a CI job, on purpose. A GitHub runner's throughput varies by more than the 5% this
// checks for, so every threshold would be noise and the job would be muted inside a month.
// This is the thing a person runs before and after an upgrade, on one machine, which is what
// spec 012 R16 was asking for and could not have.

import { dirname, fromFileUrl, join } from "@std/path";

import { drive } from "./load.ts";
import { startRssSampler } from "./rss.ts";
import { findPublishedExe, publish, startApp, waitForHealthz } from "./server.ts";
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
  port: number;
  harness: HarnessSettings;
}

export const DEFAULTS: Options = {
  check: false,
  writeBaseline: false,
  skipPublish: false,
  port: 5997,
  // 16 workers, 10 seconds, 3 runs -- section 14's own "three runs per test" discipline.
  harness: { concurrency: 16, durationMs: 10_000, runs: 3 },
};

export function parseArgs(args: string[]): Options {
  const options: Options = { ...DEFAULTS, harness: { ...DEFAULTS.harness } };

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    const value = () => {
      const next = args[++i];
      if (next === undefined) throw new Error(`${arg} needs a value.`);
      const parsed = Number(next);
      if (!Number.isFinite(parsed) || parsed <= 0) throw new Error(`${arg} needs a positive number, got '${next}'.`);
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
      case "--port":
        options.port = value();
        break;
      case "--duration":
        options.harness.durationMs = value() * 1000;
        break;
      case "--runs":
        options.harness.runs = value();
        break;
      case "--concurrency":
        options.harness.concurrency = value();
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

  // The app's listener presents a cert this run just minted, so the driver has to trust that
  // one specifically. Not a global "ignore certificate errors": the harness talks to loopback
  // and to nothing else, and a client that accepts anything is a habit worth not forming.
  const client = Deno.createHttpClient({ caCerts: [app.cert.pem] });
  const fetchImpl: typeof fetch = (input, init) =>
    fetch(input as string, { ...init, client } as unknown as RequestInit);

  try {
    const readyMs = await waitForHealthz(app.baseUrl, 60_000, fetchImpl);
    console.log(`Up in ${Math.round(readyMs)}ms on ${app.baseUrl} (pid ${app.pid})\n`);

    for (const [name, path] of Object.entries(SCENARIOS)) {
      const url = `${app.baseUrl}${path}`;
      const { concurrency, durationMs, runs } = options.harness;

      // One discarded warm-up pass. Section 14 caught V8 climbing 1,291 -> 1,793 req/s across
      // three back-to-back runs; NativeAOT has no equivalent curve, but the OS page cache and
      // the socket pool do, and a cold first pass would drag the average down by more than
      // the 5% this harness is trying to detect.
      console.log(`${name}: warming up...`);
      await drive({ url, concurrency, durationMs: Math.min(durationMs, 3000), fetchImpl });

      const sampler = startRssSampler(app.pid, 250);
      const samples = [];
      for (let run = 1; run <= runs; run++) {
        const sample = await drive({ url, concurrency, durationMs, fetchImpl });
        const rps = Math.round((sample.requests / sample.elapsedMs) * 1000);
        console.log(`${name}: run ${run}/${runs} -- ${rps} req/s, ${sample.errors} errors`);
        samples.push(sample);
      }
      const maxRssBytes = await sampler.stop();

      measurements[name] = summarize(samples, maxRssBytes);
      console.log("");
    }
  } finally {
    client.close();
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

async function readBaseline(root: string): Promise<Baseline | null> {
  try {
    return JSON.parse(await Deno.readTextFile(baselinePath(root))) as Baseline;
  } catch {
    return null;
  }
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
      + "the same machine and the same harness settings, 15% down on req/s and 25% up on RSS. "
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
    const objection = baselineObjection(measurements);
    if (objection !== null) {
      console.error(`Refusing to record a baseline: ${objection}`);
      return 2;
    }

    await Deno.writeTextFile(baselinePath(root), `${JSON.stringify(record, null, 2)}
`);
    console.log(`Recorded ${baselinePath(root)}`);
    return 0;
  }

  const baseline = await readBaseline(root);
  if (baseline === null) {
    console.log("No baseline.json yet -- run with --baseline to record this measurement as one.");
    return options.check ? 2 : 0;
  }

  const fit = comparable(baseline, machine, options.harness);
  if (!fit.ok) {
    console.log(`Record-only: ${fit.reason}. Nothing to compare against.`);
    return 0;
  }

  const regressions = compare(baseline.scenarios, measurements);
  if (regressions.length === 0) {
    console.log(`Within tolerance of the baseline recorded ${baseline.recorded}.`);
    return 0;
  }

  for (const r of regressions) {
    console.error(`REGRESSION ${r.scenario} (${r.metric}): ${r.detail}`);
  }
  return options.check ? 1 : 0;
}

if (import.meta.main) {
  try {
    Deno.exit(await main(Deno.args));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    Deno.exit(2);
  }
}
