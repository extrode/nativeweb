import { assertEquals, assertStringIncludes, assertThrows } from "jsr:@std/assert@1";

import {
  DATABASE_FREE_FILTER,
  diagnosticsIn,
  occupants,
  parseArgs,
  stampEnvironment,
  stampStepEnvironment,
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

Deno.test("the database-free filter excludes readyz and nothing else", () => {
  assertEquals(DATABASE_FREE_FILTER, "FullyQualifiedName!~Readyz");
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
