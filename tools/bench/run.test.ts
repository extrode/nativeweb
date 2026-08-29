import { assert, assertEquals, assertStringIncludes, assertThrows } from "@std/assert";

import type { ScenarioMeasurement } from "./stats.ts";

import {
  baselineObjection,
  DEFAULTS,
  fingerprint,
  overwriteObjection,
  parseArgs,
  repoRoot,
  SCENARIOS,
} from "./run.ts";

Deno.test("no arguments is measure-and-report, not check and not baseline", () => {
  const options = parseArgs([]);
  assertEquals(options.check, false);
  assertEquals(options.writeBaseline, false);
  assertEquals(options.skipPublish, false);
  assertEquals(options.harness, DEFAULTS.harness);
});

Deno.test("--duration is seconds on the command line and milliseconds inside", () => {
  assertEquals(parseArgs(["--duration", "3"]).harness.durationMs, 3000);
});

Deno.test("--runs, --concurrency and --port are read", () => {
  const options = parseArgs(["--runs", "5", "--concurrency", "32", "--port", "6100"]);
  assertEquals(options.harness.runs, 5);
  assertEquals(options.harness.concurrency, 32);
  assertEquals(options.port, 6100);
});

Deno.test("--check and --baseline together is refused rather than silently picking one", () => {
  const error = assertThrows(() => parseArgs(["--check", "--baseline"]), Error);
  assert(error.message.includes("opposites"));
});

Deno.test("an unknown flag stops the run instead of being ignored", () => {
  // A typo'd --skip-publish that silently republished would waste a minute; a typo'd
  // --check that silently passed would report a regression as a success.
  assertThrows(() => parseArgs(["--skipublish"]), Error);
});

Deno.test("a flag needing a value rejects a missing or nonsense one", () => {
  assertThrows(() => parseArgs(["--duration"]), Error);
  assertThrows(() => parseArgs(["--duration", "soon"]), Error);
  assertThrows(() => parseArgs(["--runs", "0"]), Error);
  assertThrows(() => parseArgs(["--concurrency", "-4"]), Error);
});

Deno.test("the fingerprint carries everything the comparison gates on", () => {
  const machine = fingerprint();
  assert(machine.hostname.length > 0);
  assert(machine.cpus > 0);
  assertEquals(machine.os, Deno.build.os);
  assertEquals(machine.arch, Deno.build.arch);
});

Deno.test("repoRoot walks up to the solution file", () => {
  const root = repoRoot();
  assertEquals(Deno.statSync(`${root}/NativeWeb.slnx`).isFile, true);
});

Deno.test("repoRoot fails rather than looping when there is no solution above", () => {
  // The OS temp directory, not the repo's tmp/ as elsewhere in these tests: repoRoot walks
  // upwards, so a repo-local directory has NativeWeb.slnx above it and would not throw.
  const outside = Deno.makeTempDirSync({ prefix: "bench-no-slnx-" });
  try {
    assertThrows(() => repoRoot(outside), Error);
  } finally {
    Deno.removeSync(outside, { recursive: true });
  }
});

Deno.test("both section 14 shapes are measured: the render and the floor", () => {
  assertEquals(SCENARIOS.home, "/");
  assertEquals(SCENARIOS.healthz, "/healthz");
});

function measurement(over: Partial<ScenarioMeasurement> = {}): ScenarioMeasurement {
  return { rps: 1000, p50Ms: 1, p99Ms: 2, errors: 0, maxRssBytes: 50 * 1024 * 1024, ...over };
}

Deno.test("a clean measurement may become a baseline", () => {
  assertEquals(baselineObjection({ home: measurement() }), null);
});

Deno.test("a measurement with failed requests is refused as a baseline", () => {
  // The first real run of this harness recorded 0 req/s and 245,106 errors as a baseline,
  // because every GET / answered 500 over http. Nothing stopped it.
  const objection = baselineObjection({ home: measurement({ rps: 0, errors: 245_106 }) });
  assert(objection !== null);
  assert(objection.includes("245106"), objection);
});

Deno.test("a measurement that served nothing is refused even with no errors recorded", () => {
  assert(baselineObjection({ home: measurement({ rps: 0 }) }) !== null);
});

Deno.test("a measurement whose RSS never read is refused", () => {
  assert(baselineObjection({ home: measurement({ maxRssBytes: 0 }) }) !== null);
});

Deno.test("an empty measurement is refused rather than written as an empty baseline", () => {
  assert(baselineObjection({}) !== null);
});

Deno.test("--force is off by default and read when passed", () => {
  assertEquals(parseArgs([]).force, false);
  assertEquals(parseArgs(["--baseline", "--force"]).force, true);
});

Deno.test("--baseline refuses to replace another machine's committed baseline", () => {
  const objection = overwriteObjection(
    {
      recorded: "2026-08-29",
      machine: { hostname: "bench-host", cpus: 16, os: "windows", arch: "x86_64" },
      harness: DEFAULTS.harness,
      scenarios: { home: measurement() },
    },
    { hostname: "runner-7", cpus: 4, os: "linux", arch: "x86_64" },
    false,
  );

  // Otherwise a --baseline anywhere else replaces the file, and every later --check on bench-host
  // exits 0 having compared nothing: the gate does not break, it goes quiet.
  assert(objection !== null);
  assertStringIncludes(objection, "bench-host");
  assertStringIncludes(objection, "runner-7");
  assertStringIncludes(objection, "--force");
});

Deno.test("--baseline on the recording machine, and --force anywhere, are allowed", () => {
  const machine = { hostname: "bench-host", cpus: 16, os: "windows", arch: "x86_64" };
  const existing = {
    recorded: "2026-08-29",
    machine,
    harness: DEFAULTS.harness,
    scenarios: { home: measurement() },
  };

  assertEquals(overwriteObjection(existing, { ...machine }, false), null);
  assertEquals(overwriteObjection(existing, { hostname: "other", cpus: 4, os: "linux", arch: "x86_64" }, true), null);
  // Nothing committed yet: the first baseline is always allowed.
  assertEquals(overwriteObjection(null, machine, false), null);
});
