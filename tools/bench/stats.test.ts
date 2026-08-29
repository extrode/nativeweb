import { assertEquals, assertStringIncludes } from "@std/assert";

import {
  type Baseline,
  comparable,
  compare,
  type HarnessSettings,
  type Machine,
  mean,
  percentile,
  RPS_FLOOR,
  RSS_CEILING,
  type RunSample,
  type ScenarioMeasurement,
  summarize,
} from "./stats.ts";

const machine: Machine = { hostname: "bench-1", cpus: 16, os: "windows", arch: "x86_64" };
const harness: HarnessSettings = { concurrency: 16, durationMs: 10_000, runs: 3 };

function measurement(over: Partial<ScenarioMeasurement> = {}): ScenarioMeasurement {
  return { rps: 1000, p50Ms: 1, p99Ms: 5, errors: 0, maxRssBytes: 100 * 1024 * 1024, ...over };
}

function sample(over: Partial<RunSample> = {}): RunSample {
  return { requests: 1000, errors: 0, elapsedMs: 1000, latenciesMs: [1, 2, 3], ...over };
}

Deno.test("percentile takes the nearest rank and does not need a sorted input", () => {
  const values = [9, 1, 5, 3, 7];
  assertEquals(percentile(values, 50), 5);
  assertEquals(percentile(values, 100), 9);
  assertEquals(percentile(values, 1), 1);
});

Deno.test("percentile of an empty sample is 0, not NaN", () => {
  assertEquals(percentile([], 99), 0);
  assertEquals(mean([]), 0);
});

Deno.test("summarize averages per-run rates and pools the latencies", () => {
  const result = summarize([
    sample({ requests: 1000, elapsedMs: 1000, latenciesMs: [1, 1] }),
    // Half the requests in half the time: the same rate, so the average must stay 1000.
    sample({ requests: 500, elapsedMs: 500, latenciesMs: [100] }),
  ], 42);

  assertEquals(result.rps, 1000);
  // Pooled: three samples, so p99 is the largest. A percentile of per-run percentiles
  // would have lost the 100.
  assertEquals(result.p99Ms, 100);
  assertEquals(result.maxRssBytes, 42);
});

Deno.test("summarize sums errors across runs", () => {
  const result = summarize([sample({ errors: 2 }), sample({ errors: 3 })], 1);
  assertEquals(result.errors, 5);
});

Deno.test("a run that recorded no elapsed time contributes a zero rate, not a division by zero", () => {
  const result = summarize([sample({ elapsedMs: 0, requests: 10 })], 1);
  assertEquals(result.rps, 0);
});

Deno.test("throughput exactly at the floor passes, a hair under fails", () => {
  const baseline = { home: measurement({ rps: 1000 }) };

  assertEquals(compare(baseline, { home: measurement({ rps: 950 }) }, 0.95).length, 0);

  const under = compare(baseline, { home: measurement({ rps: 949.9 }) }, 0.95);
  assertEquals(under.length, 1);
  assertEquals(under[0].metric, "rps");
  assertStringIncludes(under[0].detail, "949.9");
});

Deno.test("throughput above the baseline is not a regression", () => {
  const baseline = { home: measurement({ rps: 1000 }) };
  assertEquals(compare(baseline, { home: measurement({ rps: 5000 }) }).length, 0);
});

Deno.test("RSS exactly at the ceiling passes, a hair over fails", () => {
  const base = 100 * 1024 * 1024;
  const baseline = { home: measurement({ maxRssBytes: base }) };

  assertEquals(
    compare(baseline, { home: measurement({ maxRssBytes: base * 1.05 }) }, 0.95, 1.05).length,
    0,
  );

  const over = compare(baseline, { home: measurement({ maxRssBytes: base * 1.05 + 1 }) }, 0.95, 1.05);
  assertEquals(over.length, 1);
  assertEquals(over[0].metric, "rss");
});

Deno.test("RSS below the baseline is not a regression", () => {
  const baseline = { home: measurement({ maxRssBytes: 100 * 1024 * 1024 }) };
  assertEquals(compare(baseline, { home: measurement({ maxRssBytes: 1024 }) }).length, 0);
});

Deno.test("any error at all is a regression, however good the throughput looks", () => {
  const baseline = { home: measurement({ rps: 1000 }) };
  const result = compare(baseline, { home: measurement({ rps: 9999, errors: 1 }) });
  assertEquals(result.length, 1);
  assertEquals(result[0].metric, "errors");
});

Deno.test("a scenario the baseline has and the run does not is a regression, not a pass", () => {
  const result = compare({ home: measurement() }, {});
  assertEquals(result.length, 1);
  assertEquals(result[0].metric, "missing");
});

Deno.test("a scenario the run has and the baseline does not is ignored", () => {
  assertEquals(compare({}, { newthing: measurement({ rps: 1 }) }).length, 0);
});

Deno.test("comparable accepts an identical machine and harness", () => {
  const baseline: Baseline = { recorded: "2026-08-29", machine, harness, scenarios: {} };
  assertEquals(comparable(baseline, machine, harness).ok, true);
});

Deno.test("comparable rejects a different machine and names both", () => {
  const baseline: Baseline = { recorded: "2026-08-29", machine, harness, scenarios: {} };
  const other = { ...machine, hostname: "runner-7" };

  const fit = comparable(baseline, other, harness);
  assertEquals(fit.ok, false);
  if (!fit.ok) {
    assertStringIncludes(fit.reason, "bench-1");
    assertStringIncludes(fit.reason, "runner-7");
  }
});

Deno.test("comparable rejects a different core count on the same hostname", () => {
  const baseline: Baseline = { recorded: "2026-08-29", machine, harness, scenarios: {} };
  assertEquals(comparable(baseline, { ...machine, cpus: 8 }, harness).ok, false);
});

Deno.test("comparable rejects a shorter run against a longer baseline", () => {
  const baseline: Baseline = { recorded: "2026-08-29", machine, harness, scenarios: {} };
  const fit = comparable(baseline, machine, { ...harness, durationMs: 2000 });
  assertEquals(fit.ok, false);
  if (!fit.ok) assertStringIncludes(fit.reason, "2000ms");
});

Deno.test("comparable rejects a different concurrency", () => {
  const baseline: Baseline = { recorded: "2026-08-29", machine, harness, scenarios: {} };
  assertEquals(comparable(baseline, machine, { ...harness, concurrency: 4 }).ok, false);
});

Deno.test("the shipped tolerances are the measured ones, not round numbers", () => {
  // Eleven home passes on one unchanged binary spread 16.3% and peak RSS 29%, so a 5% band on
  // either would fail on noise. Pinned here because widening a tolerance quietly is how a
  // check stops meaning anything.
  assertEquals(RPS_FLOOR, 0.80);
  assertEquals(RSS_CEILING, 1.25);

  const baseline = { home: measurement({ rps: 1000, maxRssBytes: 100 * 1024 * 1024 }) };
  // The observed extremes, 16.3% down and 24% up: both inside the shipped band.
  assertEquals(
    compare(baseline, { home: measurement({ rps: 837, maxRssBytes: 124 * 1024 * 1024 }) }).length,
    0,
  );
  // A 25% throughput drop is larger than anything this harness has measured as noise.
  assertEquals(compare(baseline, { home: measurement({ rps: 750 }) }).length, 1);
});
