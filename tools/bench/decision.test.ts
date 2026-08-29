// The exit-code routing, which is where a benchmark harness fails quietly if it fails at all:
// a check that reads green because it compared nothing looks identical to one that passed.

import { assert, assertEquals, assertRejects, assertStringIncludes } from "@std/assert";
import { join } from "@std/path";

import { decide, DEFAULTS, isBaseline, type Options, readBaseline } from "./run.ts";
import type { Baseline, Machine, ScenarioMeasurement } from "./stats.ts";
import { removeScratch, scratchDir } from "./scratch.ts";

const machine: Machine = { hostname: "bench-1", cpus: 16, os: "windows", arch: "x86_64" };

function options(over: Partial<Options> = {}): Options {
  return { ...DEFAULTS, harness: { ...DEFAULTS.harness }, ...over };
}

function measurement(over: Partial<ScenarioMeasurement> = {}): ScenarioMeasurement {
  return { rps: 1000, p50Ms: 1, p99Ms: 2, errors: 0, maxRssBytes: 50 * 1024 * 1024, ...over };
}

function baseline(over: Partial<Baseline> = {}): Baseline {
  return {
    recorded: "2026-08-29",
    machine,
    harness: { ...DEFAULTS.harness },
    scenarios: { home: measurement() },
    ...over,
  };
}

Deno.test("no baseline under --check is exit 2, not a pass", () => {
  // The state where a green exit would mean "measured and fine" while nothing was compared.
  const decision = decide(options({ check: true }), null, machine, { home: measurement() });
  assertEquals(decision.code, 2);
  assert(decision.failed);
});

Deno.test("no baseline without --check is exit 0 and says how to record one", () => {
  const decision = decide(options(), null, machine, { home: measurement() });
  assertEquals(decision.code, 0);
  assertEquals(decision.failed, false);
  assertStringIncludes(decision.message, "--baseline");
});

Deno.test("another machine's baseline is record-only, even under --check", () => {
  const elsewhere = baseline({ machine: { ...machine, hostname: "runner-7" } });
  const decision = decide(options({ check: true }), elsewhere, machine, {
    // Half the baseline's throughput: it must still not fail, because the comparison is
    // meaningless across hardware and a false red is how a check gets muted.
    home: measurement({ rps: 500 }),
  });

  assertEquals(decision.code, 0);
  assertEquals(decision.failed, false);
  assertStringIncludes(decision.message, "Record-only");
});

Deno.test("a shortened --check run is exit 2, not the record-only pass a machine mismatch gets", () => {
  // The escape hatch this closes: --check --duration 5 against a 10-second baseline used to
  // take the record-only branch and exit 0, so the check was optional for anyone who habitually
  // shortens a run. A machine mismatch is a fact about the hardware; this is a flag the
  // operator typed on this invocation, and the two must not share an exit code.
  const decision = decide(
    options({ check: true, harness: { ...DEFAULTS.harness, durationMs: 5_000 } }),
    baseline(),
    machine,
    { home: measurement({ rps: 500 }) },
  );

  assertEquals(decision.code, 2);
  assert(decision.failed);
  assertStringIncludes(decision.message, "Nothing was compared");
});

Deno.test("the same shortened run without --check still records rather than failing", () => {
  const decision = decide(
    options({ harness: { ...DEFAULTS.harness, runs: 1 } }),
    baseline(),
    machine,
    { home: measurement() },
  );

  assertEquals(decision.code, 0);
  assertEquals(decision.failed, false);
  assertStringIncludes(decision.message, "Record-only");
});

Deno.test("a regression under --check is exit 1 and names the scenario", () => {
  const decision = decide(options({ check: true }), baseline(), machine, {
    home: measurement({ rps: 500 }),
  });

  assertEquals(decision.code, 1);
  assert(decision.failed);
  assertStringIncludes(decision.message, "REGRESSION home");
});

Deno.test("the same regression without --check reports but exits 0", () => {
  const decision = decide(options(), baseline(), machine, { home: measurement({ rps: 500 }) });

  assertEquals(decision.code, 0);
  // Still surfaced as a failure line, so a bare run does not hide it.
  assert(decision.failed);
  assertStringIncludes(decision.message, "REGRESSION home");
});

Deno.test("a measurement within tolerance is exit 0 and names the baseline's date", () => {
  const decision = decide(options({ check: true }), baseline(), machine, {
    home: measurement({ rps: 980 }),
  });

  assertEquals(decision.code, 0);
  assertEquals(decision.failed, false);
  assertStringIncludes(decision.message, "2026-08-29");
});

Deno.test("readBaseline returns null only when the file is absent", async () => {
  const dir = await scratchDir();
  try {
    assertEquals(await readBaseline(join(dir, "nothing.json")), null);
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("a malformed baseline throws instead of reading as absent", async () => {
  const dir = await scratchDir();
  try {
    // Reported as "no baseline yet", this would invite --baseline to overwrite a file that
    // exists -- which on a half-written baseline is the last copy of the measurement.
    const path = join(dir, "baseline.json");
    await Deno.writeTextFile(path, '{"recorded": "2026-08-29", "mach');

    const error = await assertRejects(() => readBaseline(path), Error);
    assertStringIncludes(error.message, "not valid JSON");
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("a baseline missing its machine block is refused by name", async () => {
  const dir = await scratchDir();
  try {
    const path = join(dir, "baseline.json");
    await Deno.writeTextFile(path, JSON.stringify({ recorded: "2026-08-29", scenarios: {} }));

    const error = await assertRejects(() => readBaseline(path), Error);
    assertStringIncludes(error.message, "--baseline");
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("readBaseline round-trips a real one", async () => {
  const dir = await scratchDir();
  try {
    const path = join(dir, "baseline.json");
    await Deno.writeTextFile(path, JSON.stringify(baseline()));
    assertEquals((await readBaseline(path))?.recorded, "2026-08-29");
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("isBaseline rejects what would crash the comparison", () => {
  assertEquals(isBaseline(null), false);
  assertEquals(isBaseline("a string"), false);
  assertEquals(isBaseline({}), false);
  assertEquals(isBaseline({ ...baseline(), machine: undefined }), false);
  assertEquals(isBaseline({ ...baseline(), harness: { concurrency: 16 } }), false);
  assertEquals(isBaseline({ ...baseline(), scenarios: null }), false);
  assertEquals(isBaseline(baseline()), true);
});

Deno.test("isBaseline rejects a scenario whose numbers are not numbers", () => {
  // The envelope check passed all of these, and then `base.rps * 0.80` was NaN and
  // `now.rps < NaN` was false for every measurement that could ever be taken. The gate was
  // unfailable from that edit onward, with the file still round-tripping as a baseline.
  const quoted = { ...baseline(), scenarios: { home: { ...measurement(), rps: "1000" } } };
  const absent = { ...baseline(), scenarios: { home: { ...measurement(), maxRssBytes: null } } };
  const notFinite = { ...baseline(), scenarios: { home: { ...measurement(), rps: NaN } } };
  const partial = { ...baseline(), scenarios: { home: { rps: 1000 } } };
  const oneBadOfTwo = {
    ...baseline(),
    scenarios: { home: measurement(), healthz: { ...measurement(), p99Ms: "2" } },
  };

  assertEquals(isBaseline(quoted), false);
  assertEquals(isBaseline(absent), false);
  assertEquals(isBaseline(notFinite), false);
  assertEquals(isBaseline(partial), false);
  assertEquals(isBaseline(oneBadOfTwo), false);
});

Deno.test("the committed baseline is a valid one", async () => {
  // The file R16 rests on. A hand edit that broke its shape would otherwise only show up on
  // somebody's next benchmark run.
  const { repoRoot, baselinePath } = await import("./run.ts");
  const committed = await readBaseline(baselinePath(repoRoot()));

  assert(committed !== null, "tools/bench/baseline.json is missing");
  assert(Object.keys(committed.scenarios).length >= 2, "both scenarios should be recorded");
  for (const [name, m] of Object.entries(committed.scenarios)) {
    assertEquals(m.errors, 0, `${name} recorded errors`);
    assert(m.rps > 0, `${name} recorded no throughput`);
    assert(m.maxRssBytes > 0, `${name} recorded no RSS`);
  }
});
