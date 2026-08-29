import { assert, assertEquals, assertThrows } from "@std/assert";

import type { ScenarioMeasurement } from "./stats.ts";

import { baselineObjection, DEFAULTS, fingerprint, parseArgs, repoRoot, SCENARIOS } from "./run.ts";

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
  assertThrows(() => repoRoot(Deno.makeTempDirSync()), Error);
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
