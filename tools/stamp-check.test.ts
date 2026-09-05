import { assertEquals, assertStringIncludes, assertThrows } from "jsr:@std/assert@1";

import {
  DATABASE_FREE_FILTER,
  databaseHint,
  diagnosticsIn,
  occupants,
  parseArgs,
  stampEnvironment,
  stampStepEnvironment,
  testsRun,
} from "./stamp-check.ts";

Deno.test("parseArgs stamps Stampcheck into tmp by default", () => {
  const options = parseArgs([]);

  assertEquals(options.name, "Stampcheck");
  assertEquals(options.slug, "stampcheck");
  assertEquals(options.destination, null);
  assertEquals(options.withDatabase, false);
});

Deno.test("parseArgs takes a name, a slug and a destination", () => {
  const options = parseArgs(["--name", "Widgets", "--slug", "widgets", "--dest", "/tmp/w"]);

  assertEquals(options.name, "Widgets");
  assertEquals(options.slug, "widgets");
  assertEquals(options.destination, "/tmp/w");
});

Deno.test("parseArgs refuses a name new-product.ps1 would refuse", () => {
  const error = assertThrows(() => parseArgs(["--name", "widgets"]));

  assertStringIncludes((error as Error).message, "PascalCase");
});

Deno.test("parseArgs refuses a slug with a hyphen, which becomes a Postgres role", () => {
  const error = assertThrows(() => parseArgs(["--slug", "wid-gets"]));

  assertStringIncludes((error as Error).message, "lowercase");
});

Deno.test("parseArgs refuses an argument it does not know rather than ignoring it", () => {
  assertThrows(() => parseArgs(["--execute"]));
});

Deno.test("parseArgs refuses a flag whose value is missing", () => {
  const error = assertThrows(() => parseArgs(["--dest"]));

  assertStringIncludes((error as Error).message, "needs a value");
});

Deno.test("stampEnvironment resolves NativeWeb by checkout and JauntyQ by packages", () => {
  const env = stampEnvironment("C:/repo");

  assertEquals(env.NativeWebRoot, "C:/repo");
  assertEquals(env.JauntyQUsePackages, "true");
  assertEquals(env.NativeWebUsePackages, "");
});

Deno.test("stampEnvironment blanks the switches an exporting shell would flip", () => {
  const env = stampEnvironment("C:/repo");

  assertEquals(env.NativeWebUsePackages, "");
  assertEquals(env.JauntyQRoot, "");
});

Deno.test("only the stamp step may commit to a protected branch", () => {
  assertEquals(stampEnvironment("C:/repo").GIT_ALLOW_PROTECTED, undefined);
  assertEquals(stampStepEnvironment("C:/repo").GIT_ALLOW_PROTECTED, "1");
});

Deno.test("the stamp step still resolves NativeWeb by checkout", () => {
  assertEquals(stampStepEnvironment("C:/repo").NativeWebRoot, "C:/repo");
});

Deno.test("the database-free filter excludes exactly one test in the skeleton tier", async () => {
  const source = await Deno.readTextFile(
    new URL("../skeleton/tests/__product__.tests.integration/SmokeTests.cs", import.meta.url),
  );

  const names = [...source.matchAll(/public\s+(?:async\s+)?Task\s+(\w+)\s*\(/g)].map((m) => m[1]);
  const term = DATABASE_FREE_FILTER.split("!~")[1];
  const excluded = names.filter((name) => name.includes(term));

  assertEquals(names.length > 1, true);
  assertEquals(excluded, ["Readyz_ReportsReady"]);
});

// Verbatim from `dotnet test --project tests/nativeweb.sample.tests.unit`, not
// paraphrased: the leading spaces and the lower-case keys are the whole reason
// the VSTest regex this replaced no longer matches anything.
const MTP_PASSING = [
  "x.dll (net10.0|x64) passed (556ms)",
  "",
  "Test run summary: Passed!",
  "  total: 148",
  "  failed: 0",
  "  succeeded: 148",
  "  skipped: 0",
  "  duration: 743ms",
].join("\n");

const MTP_ZERO = [
  "x.dll (net10.0|x64) Zero tests ran (193ms)",
  "",
  "Test run summary: Zero tests ran",
  "  total: 0",
  "  failed: 0",
  "  succeeded: 0",
  "  skipped: 0",
].join("\n");

Deno.test("testsRun reads the count a passing tier reports", () => {
  assertEquals(testsRun(MTP_PASSING), 148);
});

Deno.test("testsRun answers zero for a filter that matched nothing", () => {
  assertEquals(testsRun(MTP_ZERO), 0);
});

Deno.test("testsRun falls back to the platform's words when it prints no count block", () => {
  assertEquals(testsRun("x.dll (net10.0|x64) Zero tests ran (193ms)"), 0);
});

Deno.test("testsRun reads the last summary block, not the first", () => {
  assertEquals(testsRun("  total: 3\n  failed: 0\n\nTest run summary\n  total: 17\n"), 17);
});

Deno.test("testsRun answers null for output that is not a test run", () => {
  assertEquals(testsRun("Build succeeded."), null);
});

// VSTest's summary is one line with capitalised keys and no `total:` of its own.
// Reading it as a count would be worse than reading nothing: the old shape can
// only reach this function if something has pinned the runner back, and a number
// scraped out of it would report a tier as run when nothing under MTP ran it.
Deno.test("testsRun does not read a VSTest summary as a count", () => {
  assertEquals(testsRun("Passed!  - Failed: 0, Passed: 148, Skipped: 0, Total: 148"), null);
});

Deno.test("diagnosticsIn catches an MSBuild warning that carries no code", () => {
  assertEquals(
    diagnosticsIn("Directory.Build.props(5,3): warning : the skeleton pins a stale version"),
    ["Directory.Build.props(5,3): warning : the skeleton pins a stale version"],
  );
});

Deno.test("the database hint names the database and the variable the tier reads", () => {
  const lines = databaseHint({ name: "Widgets", slug: "widgets" }).join("\n");

  assertStringIncludes(lines, "CREATE DATABASE widgets_test");
  assertStringIncludes(lines, "Widgets__Db__ConnectionString");
  assertStringIncludes(lines, "Database=widgets_test");
});

Deno.test("diagnosticsIn reports a build error and ignores a path that reads like one", () => {
  assertEquals(diagnosticsIn("  0 Warning(s)\n  /src/ErrorPageMiddleware.cs\n"), []);
  assertEquals(
    diagnosticsIn("Program.cs(12,5): error CS0246: type not found"),
    ["Program.cs(12,5): error CS0246: type not found"],
  );
});

Deno.test("occupants answers nothing for a destination that does not exist", async () => {
  assertEquals(await occupants(`${await Deno.makeTempDir()}/absent`), []);
});

Deno.test("occupants treats a file where a directory was named as something in the way", async () => {
  const path = `${await Deno.makeTempDir()}/not-a-directory`;

  await Deno.writeTextFile(path, "");

  const found = await occupants(path);

  assertEquals(found.length, 1);
  assertStringIncludes(found[0], "could not be read as a directory");
});

Deno.test("occupants names what is in the way", async () => {
  const directory = await Deno.makeTempDir();

  await Deno.writeTextFile(`${directory}/README.md`, "");
  await Deno.mkdir(`${directory}/apps`);

  assertEquals(await occupants(directory), ["README.md", "apps"]);
});
