import { assert, assertEquals, assertStringIncludes, assertThrows } from "@std/assert";

import type { ScenarioMeasurement } from "./stats.ts";

import {
  baselineObjection,
  DEFAULTS,
  fingerprint,
  localDate,
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
  assertEquals(SCENARIOS.articles, "/articles");
  assertEquals(SCENARIOS.healthz, "/healthz");
});

// "/" on apps/sample is a 302 to /admin, so a scenario pointed at it would measure the router
// and report a redirect as a successful request. The render scenario was "/" while apps/web was
// the target and that app rendered its home page; this pins the swap rather than leaving the two
// facts to look interchangeable.
Deno.test("the render scenario is not the redirect at the root", () => {
  assertEquals(Object.values(SCENARIOS).includes("/"), false);
});

function measurement(over: Partial<ScenarioMeasurement> = {}): ScenarioMeasurement {
  return { rps: 1000, p50Ms: 1, p99Ms: 2, errors: 0, maxRssBytes: 50 * 1024 * 1024, ...over };
}

Deno.test("a clean measurement may become a baseline", () => {
  assertEquals(baselineObjection({ articles: measurement() }), null);
});

Deno.test("a measurement with failed requests is refused as a baseline", () => {
  // The first real run of this harness recorded 0 req/s and 245,106 errors as a baseline,
  // because every GET / answered 500 over http. Nothing stopped it.
  const objection = baselineObjection({ articles: measurement({ rps: 0, errors: 245_106 }) });
  assert(objection !== null);
  assert(objection.includes("245106"), objection);
});

Deno.test("a measurement that served nothing is refused even with no errors recorded", () => {
  assert(baselineObjection({ articles: measurement({ rps: 0 }) }) !== null);
});

Deno.test("a measurement whose RSS never read is refused", () => {
  assert(baselineObjection({ articles: measurement({ maxRssBytes: 0 }) }) !== null);
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
      scenarios: { articles: measurement() },
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
    scenarios: { articles: measurement() },
  };

  assertEquals(overwriteObjection(existing, { ...machine }, false), null);
  assertEquals(overwriteObjection(existing, { hostname: "other", cpus: 4, os: "linux", arch: "x86_64" }, true), null);
  // Nothing committed yet: the first baseline is always allowed.
  assertEquals(overwriteObjection(null, machine, false), null);
});

// The baseline's own date stamp. It was new Date().toISOString().slice(0, 10) -- the UTC date --
// and on a UTC+8 machine a run made on 2026-08-31 stamped itself 2026-08-30, so --check reported
// "within tolerance of the baseline recorded 2026-08-30" about a file written minutes before.
// The three cases below are the ones that separate a local date from a UTC one.
Deno.test("the baseline is stamped with the local date, not the UTC one", () => {
  // 2026-08-31 07:00 local on a UTC+8 machine is 2026-08-30T23:00Z. Constructed from local
  // components so the test asserts the same thing whatever timezone it runs in.
  const earlyMorning = new Date(2026, 7, 31, 7, 0, 0);
  assertEquals(localDate(earlyMorning), "2026-08-31");

  // Late evening, which is where a machine WEST of UTC crosses the other way.
  const lateEvening = new Date(2026, 7, 31, 23, 30, 0);
  assertEquals(localDate(lateEvening), "2026-08-31");

  // Zero-padding: a single-digit month and day must not emit "2026-1-5".
  assertEquals(localDate(new Date(2026, 0, 5, 12, 0, 0)), "2026-01-05");
});
