// The arithmetic, kept away from sockets and processes so it can be tested without either.
//
// Spec 012 R16 asked for the section 14 benchmark re-run. Those figures came from a sandbox
// that no longer exists, so there is nothing to re-measure against; what this harness does
// instead is establish a baseline on the machine it runs on and compare against that one.
// Every threshold here is therefore RELATIVE. An absolute req/s from this file means nothing
// on its own -- a Deno fetch driver has a lower ceiling than wrk, and a laptop has a lower
// ceiling than a server. The claim the harness can make is "no worse than last time, here".

/** One timed pass of one scenario. */
export interface RunSample {
  /** Responses `fetch` reported as ok -- 2xx, redirects having been followed. */
  requests: number;
  /** Every other response, plus transport failures and aborts. */
  errors: number;
  elapsedMs: number;
  /** Wall time per SUCCESSFUL request, unsorted. */
  latenciesMs: number[];
}

/** What one scenario measured, averaged across its runs. */
export interface ScenarioMeasurement {
  rps: number;
  p50Ms: number;
  p99Ms: number;
  errors: number;
  maxRssBytes: number;
}

export interface HarnessSettings {
  concurrency: number;
  durationMs: number;
  runs: number;
}

export interface Machine {
  hostname: string;
  cpus: number;
  os: string;
  arch: string;
}

export interface Baseline {
  recorded: string;
  machine: Machine;
  harness: HarnessSettings;
  scenarios: Record<string, ScenarioMeasurement>;
}

/**
 * Nearest-rank percentile over an unsorted sample. Returns 0 for an empty sample rather
 * than NaN: a scenario that produced no responses is reported through `errors`, and a NaN
 * leaking into JSON would serialise as `null` and quietly break the comparison.
 */
export function percentile(values: number[], p: number): number {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  const rank = Math.ceil((p / 100) * sorted.length);
  const index = Math.min(sorted.length - 1, Math.max(0, rank - 1));
  return sorted[index];
}

export function mean(values: number[]): number {
  if (values.length === 0) return 0;
  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

/**
 * Fold a scenario's runs into one measurement.
 *
 * Throughput is the mean of each run's own rate, not total requests over total time --
 * section 14 averaged three runs, and a run that finished early (a crashed server, a
 * cancelled pass) would otherwise be silently weighted down rather than showing as an
 * outlier. Latency percentiles pool every run's samples, because a percentile of
 * percentiles is not a percentile.
 */
export function summarize(samples: RunSample[], maxRssBytes: number): ScenarioMeasurement {
  const rates = samples.map((s) => (s.elapsedMs > 0 ? (s.requests / s.elapsedMs) * 1000 : 0));
  const latencies = samples.flatMap((s) => s.latenciesMs);
  return {
    rps: round(mean(rates), 1),
    p50Ms: round(percentile(latencies, 50), 2),
    p99Ms: round(percentile(latencies, 99), 2),
    errors: samples.reduce((sum, s) => sum + s.errors, 0),
    maxRssBytes,
  };
}

export function round(value: number, decimals: number): number {
  const factor = 10 ** decimals;
  return Math.round(value * factor) / factor;
}

/**
 * Default tolerances, sized from this harness's own measured spread rather than from a round
 * number. Eleven `home` passes across five sessions on one machine and one unchanged binary
 * ranged 6,887 to 8,012 req/s -- 16.3% between the extremes -- and peak RSS moved 57.1 MB to
 * 73.8 MB, 29%. Within a single session, back to back, the spread is a few per cent; across
 * sessions it is whatever else the machine was doing.
 *
 * So the 5% band spec 012's close-out first proposed would fail on noise far more often than
 * on a regression. 20% down on throughput and 25% up on RSS is what survives the observed
 * spread, and it means plainly: **this harness detects gross regressions, not small ones.** A
 * doubling of page cost, a leak, a middleware that suddenly blocks -- yes. A 10% loss -- no,
 * and no honest configuration of it would.
 */
export const RPS_FLOOR = 0.80;
export const RSS_CEILING = 1.25;

export interface Regression {
  scenario: string;
  metric: "rps" | "rss" | "errors" | "missing";
  baseline: number;
  measured: number;
  detail: string;
}

/**
 * Compare a measurement against a baseline. Returns the regressions; an empty array is a pass.
 *
 * Both thresholds are one-sided on purpose. Throughput above baseline and memory below it are
 * improvements, not drift to flag -- the alternative is a harness that fails when the code
 * gets faster, which is how a check gets ignored.
 */
export function compare(
  baseline: Record<string, ScenarioMeasurement>,
  measured: Record<string, ScenarioMeasurement>,
  rpsFloor: number = RPS_FLOOR,
  rssCeiling: number = RSS_CEILING,
): Regression[] {
  const regressions: Regression[] = [];

  for (const [scenario, base] of Object.entries(baseline)) {
    const now = measured[scenario];
    if (now === undefined) {
      regressions.push({
        scenario,
        metric: "missing",
        baseline: base.rps,
        measured: 0,
        detail: `the baseline has '${scenario}' and this run did not measure it`,
      });
      continue;
    }

    if (now.errors > 0) {
      regressions.push({
        scenario,
        metric: "errors",
        baseline: 0,
        measured: now.errors,
        detail: `${now.errors} non-2xx or failed requests -- the throughput below is not a `
          + `measurement of a working server`,
      });
    }

    if (now.rps < base.rps * rpsFloor) {
      regressions.push({
        scenario,
        metric: "rps",
        baseline: base.rps,
        measured: now.rps,
        detail: `${now.rps} req/s against a baseline of ${base.rps}, below the `
          + `${pct(rpsFloor)} floor (${round(base.rps * rpsFloor, 1)})`,
      });
    }

    // A zero reading is a dead sampler, not a lean process. `0 > base * 1.25` is false, so
    // without this the RSS half of the check passes on every run where tasklist/ps failed --
    // silently, and for exactly the regression the ceiling exists to catch. baselineObjection
    // guards the write side; this is the read side of the same rule.
    if (now.maxRssBytes <= 0) {
      regressions.push({
        scenario,
        metric: "rss",
        baseline: base.maxRssBytes,
        measured: now.maxRssBytes,
        detail: `no RSS reading (the sampler returned ${now.maxRssBytes}), so the `
          + `${mb(base.maxRssBytes)} MB baseline was not checked at all`,
      });
    } else if (now.maxRssBytes > base.maxRssBytes * rssCeiling) {
      regressions.push({
        scenario,
        metric: "rss",
        baseline: base.maxRssBytes,
        measured: now.maxRssBytes,
        detail: `${mb(now.maxRssBytes)} MB peak RSS against a baseline of `
          + `${mb(base.maxRssBytes)} MB, above the ${pct(rssCeiling)} ceiling `
          + `(${mb(base.maxRssBytes * rssCeiling)} MB)`,
      });
    }
  }

  return regressions;
}

/**
 * Why a run is not comparable, when it is not. The two reasons are not equivalent and the
 * caller has to tell them apart: `machine` is a fact about where the harness is running and
 * nothing the operator can do about it, while `settings` is a flag they typed on this
 * invocation. Under `--check` the first is record-only and the second must not be, or
 * `--check --duration 5` exits green having compared nothing.
 */
export type Comparability =
  | { ok: true }
  | { ok: false; kind: "machine" | "settings"; reason: string };

/**
 * Whether a baseline was recorded somewhere this run can be compared against.
 *
 * Hardware and harness settings both count. A different core count changes the ceiling, and a
 * `--duration 2` run is not comparable to a 10-second baseline no matter what machine it ran
 * on -- a short pass carries more of the JIT/warm-up curve in its average.
 */
export function comparable(
  baseline: Baseline,
  machine: Machine,
  harness: HarnessSettings,
): Comparability {
  const b = baseline.machine;
  if (b.hostname !== machine.hostname || b.cpus !== machine.cpus || b.os !== machine.os || b.arch !== machine.arch) {
    return {
      ok: false,
      kind: "machine",
      reason: `baseline was recorded on ${describe(b)}; this is ${describe(machine)}`,
    };
  }

  const h = baseline.harness;
  if (h.concurrency !== harness.concurrency || h.durationMs !== harness.durationMs || h.runs !== harness.runs) {
    return {
      ok: false,
      kind: "settings",
      reason: `baseline used ${settings(h)}; this run used ${settings(harness)}`,
    };
  }

  return { ok: true };
}

function describe(m: Machine): string {
  return `${m.hostname} (${m.cpus} cpu, ${m.os}/${m.arch})`;
}

function settings(h: HarnessSettings): string {
  return `concurrency ${h.concurrency}, ${h.durationMs}ms x ${h.runs}`;
}

function pct(fraction: number): string {
  return `${round(fraction * 100, 1)}%`;
}

export function mb(bytes: number): number {
  return round(bytes / (1024 * 1024), 1);
}
