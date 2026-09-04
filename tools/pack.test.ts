import { assertEquals, assertRejects, assertStringIncludes, assertThrows } from "jsr:@std/assert@1";

import {
  diagnosticsIn,
  expectedVersion,
  isPackable,
  nuspecFacts,
  packageIdOf,
  parseArgs,
  readZip,
  verify,
  versionPrefixOf,
} from "./pack.ts";

Deno.test("versionPrefixOf reads the single source of truth", () => {
  assertEquals(
    versionPrefixOf("<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>"),
    "1.2.3",
  );
});

Deno.test("versionPrefixOf refuses a file with no VersionPrefix", () => {
  const error = assertThrows(() => versionPrefixOf("<Project />"));
  assertStringIncludes((error as Error).message, "found 0");
});

Deno.test("versionPrefixOf refuses a second VersionPrefix", () => {
  const error = assertThrows(() =>
    versionPrefixOf("<VersionPrefix>1.0.0</VersionPrefix><VersionPrefix>2.0.0</VersionPrefix>")
  );
  assertStringIncludes((error as Error).message, "found 2");
});

Deno.test("expectedVersion suffixes everything that is not a release", () => {
  assertEquals(expectedVersion("0.1.0", false), "0.1.0-dev");
  assertEquals(expectedVersion("0.1.0", true), "0.1.0");
});

Deno.test("isPackable reads the property, not the word", () => {
  assertEquals(isPackable("<IsPackable>true</IsPackable>"), true);
  assertEquals(isPackable("<IsPackable>false</IsPackable>"), false);
  assertEquals(isPackable("<!-- IsPackable is not set here -->"), false);
});

Deno.test("packageIdOf returns null when there is none", () => {
  assertEquals(packageIdOf("<PackageId>NativeWeb.Render</PackageId>"), "NativeWeb.Render");
  assertEquals(packageIdOf("<Project />"), null);
});

Deno.test("diagnosticsIn catches a real MSBuild diagnostic", () => {
  const output = [
    "  Determining projects to restore...",
    "C:\\x\\y.csproj : warning NU5039: The readme file does not exist.",
    "C:\\x\\z.cs(4,9): error CS0246: The type or namespace name could not be found",
  ].join("\n");

  assertEquals(diagnosticsIn(output).length, 2);
});

Deno.test("diagnosticsIn ignores lines that carry the word and mean nothing", () => {
  // Each of these is a line a real pack log contains. A filter on the bare word
  // flags all three, and the middle one is a path that exists in this
  // repository today.
  const output = [
    "  0 Warning(s)",
    "  nativeweb.web -> C:\\src\\nativeweb\\libs\\nativeweb.web\\ErrorPageMiddleware.cs",
    "  The package NativeWeb.Render.Generator.0.1.0-dev is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why.",
  ].join("\n");

  assertEquals(diagnosticsIn(output), []);
});

Deno.test("nuspecFacts reads the repository attributes and the readme", () => {
  const facts = nuspecFacts(`<package><metadata>
    <id>NativeWeb.Render</id>
    <version>0.1.0-dev</version>
    <readme>README.md</readme>
    <repository type="git" url="https://github.com/extrode/nativeweb" commit="abc123" />
  </metadata></package>`);

  assertEquals(facts.id, "NativeWeb.Render");
  assertEquals(facts.version, "0.1.0-dev");
  assertEquals(facts.readme, "README.md");
  assertEquals(facts.repositoryUrl, "https://github.com/extrode/nativeweb");
  assertEquals(facts.repositoryCommit, "abc123");
});

Deno.test("nuspecFacts reports a repository element with no url", () => {
  const facts = nuspecFacts(
    `<id>NativeWeb.Web</id><version>0.1.0</version><repository type="git" commit="abc123" />`,
  );

  assertEquals(facts.repositoryUrl, null);
  assertEquals(facts.repositoryCommit, "abc123");
  assertEquals(facts.readme, null);
});

Deno.test("parseArgs forwards what it does not own", () => {
  const options = parseArgs(["-p:JauntyQUsePackages=true", "--release", "--out", "x", "-v", "q"]);

  assertEquals(options.release, true);
  assertEquals(options.out, "x");
  assertEquals(options.forwarded, ["-p:JauntyQUsePackages=true", "-v", "q"]);
});

Deno.test("parseArgs keeps the first argument when there is no --out", () => {
  // An index-based filter drops argv[0] here, because -1 + 1 is 0. That defect
  // was in the first version of this script and no pack failure would have
  // shown it: the argument it silently dropped was package mode.
  assertEquals(parseArgs(["-p:JauntyQUsePackages=true"]).forwarded, ["-p:JauntyQUsePackages=true"]);
});

Deno.test("parseArgs refuses a trailing --out", () => {
  assertThrows(() => parseArgs(["--out"]));
});

// --- the zip reader -------------------------------------------------------

Deno.test("readZip reads a stored entry", async () => {
  const files = await readZip(await zipOf([["a.txt", "hello"]], false));

  assertEquals(new TextDecoder().decode(files.get("a.txt")), "hello");
});

Deno.test("readZip inflates a deflated entry", async () => {
  const body = "NativeWeb ".repeat(200);
  const files = await readZip(await zipOf([["big.txt", body]], true));

  assertEquals(new TextDecoder().decode(files.get("big.txt")), body);
});

Deno.test("readZip reads every entry, not only the first", async () => {
  const files = await readZip(
    await zipOf([["one.txt", "1"], ["two.txt", "2"], ["three.txt", "3"]], true),
  );

  assertEquals([...files.keys()].sort(), ["one.txt", "three.txt", "two.txt"]);
  assertEquals(new TextDecoder().decode(files.get("three.txt")), "3");
});

Deno.test("readZip finds the data past a local extra field", async () => {
  // A real .nupkg carries an extra field on every local header, and its length
  // differs from the central entry's, so the data offset has to be read from
  // the local header. Without a fixture that has one, a reader that assumes
  // zero passes every other test here and then misreads every package.
  const files = await readZip(await zipOf([["a.txt", "hello"]], false, 11));

  assertEquals(new TextDecoder().decode(files.get("a.txt")), "hello");
});

Deno.test("readZip refuses something that is not a zip", async () => {
  await assertRejects(() => readZip(new TextEncoder().encode("not a zip at all")));
});

// --- verify ---------------------------------------------------------------

Deno.test("verify accepts a package carrying everything spec 028 asks for", async () => {
  const path = await writeTempNupkg(goodNuspec(), { readme: true });

  assertEquals(
    await verify(
      { csproj: "x", directory: "y", packageId: "NativeWeb.Render", hasReadme: true },
      path,
      "0.1.0-dev",
    ),
    null,
  );
});

Deno.test("verify rejects a version that does not match", async () => {
  const path = await writeTempNupkg(goodNuspec(), { readme: true });

  const problem = await verify(
    { csproj: "x", directory: "y", packageId: "NativeWeb.Render", hasReadme: true },
    path,
    "0.2.0",
  );

  assertStringIncludes(problem ?? "", "expected 0.2.0");
});

Deno.test("verify rejects a repository element with no url", async () => {
  const path = await writeTempNupkg(
    goodNuspec().replace(' url="https://github.com/extrode/nativeweb"', ""),
    { readme: true },
  );

  const problem = await verify(
    { csproj: "x", directory: "y", packageId: "NativeWeb.Render", hasReadme: true },
    path,
    "0.1.0-dev",
  );

  assertStringIncludes(problem ?? "", "missing url or commit");
});

Deno.test("verify rejects a package whose NOTICE.md was dropped", async () => {
  const path = await writeTempNupkg(goodNuspec(), { readme: true, notice: false });

  const problem = await verify(
    { csproj: "x", directory: "y", packageId: "NativeWeb.Render", hasReadme: true },
    path,
    "0.1.0-dev",
  );

  assertStringIncludes(problem ?? "", "NOTICE.md");
});

Deno.test("verify rejects a project with a README whose package has none", async () => {
  const path = await writeTempNupkg(goodNuspec().replace("<readme>README.md</readme>", ""), {
    readme: false,
  });

  const problem = await verify(
    { csproj: "x", directory: "y", packageId: "NativeWeb.Render", hasReadme: true },
    path,
    "0.1.0-dev",
  );

  assertStringIncludes(problem ?? "", "(R4)");
});

Deno.test("verify accepts a generator, which has no README by design", async () => {
  const path = await writeTempNupkg(
    goodNuspec()
      .replace("<readme>README.md</readme>", "")
      .replace("NativeWeb.Render", "NativeWeb.Render.Generator"),
    { readme: false },
  );

  assertEquals(
    await verify(
      { csproj: "x", directory: "y", packageId: "NativeWeb.Render.Generator", hasReadme: false },
      path,
      "0.1.0-dev",
    ),
    null,
  );
});

Deno.test("verify reports a package that pack said it produced and did not", async () => {
  const problem = await verify(
    { csproj: "x", directory: "y", packageId: "NativeWeb.Render", hasReadme: true },
    `${await Deno.makeTempDir()}/absent.nupkg`,
    "0.1.0-dev",
  );

  assertStringIncludes(problem ?? "", "does not exist");
});

// --- fixtures -------------------------------------------------------------

function goodNuspec(): string {
  return `<?xml version="1.0" encoding="utf-8"?>
<package><metadata>
  <id>NativeWeb.Render</id>
  <version>0.1.0-dev</version>
  <readme>README.md</readme>
  <repository type="git" url="https://github.com/extrode/nativeweb" commit="abc123" />
</metadata></package>`;
}

async function writeTempNupkg(
  nuspec: string,
  options: { readme: boolean; notice?: boolean },
): Promise<string> {
  const id = nuspec.match(/<id>([^<]+)<\/id>/)![1];

  const entries: [string, string][] = [[`${id}.nuspec`, nuspec], ["LICENSE.md", "license"]];
  if (options.notice !== false) entries.push(["NOTICE.md", "notice"]);
  if (options.readme) entries.push(["README.md", "# readme"]);

  const path = `${await Deno.makeTempDir()}/${id}.nupkg`;
  await Deno.writeFile(path, await zipOf(entries, true));

  return path;
}

/**
 * A zip writer, so the reader is proved against bytes this file produced rather
 * than against a .nupkg a build had to make first.
 */
async function zipOf(
  entries: [string, string][],
  deflate: boolean,
  localExtraLength = 0,
): Promise<Uint8Array> {
  const encoder = new TextEncoder();
  const local: Uint8Array[] = [];
  const central: Uint8Array[] = [];
  let offset = 0;

  for (const [name, body] of entries) {
    const nameBytes = encoder.encode(name);
    const raw = encoder.encode(body);
    const data = deflate ? await deflateRaw(raw) : raw;
    const method = deflate ? 8 : 0;
    const crc = crc32(raw);

    const header = new Uint8Array(30 + nameBytes.length + localExtraLength);
    const headerView = new DataView(header.buffer);
    headerView.setUint32(0, 0x04034b50, true);
    headerView.setUint16(4, 20, true);
    headerView.setUint16(8, method, true);
    headerView.setUint32(14, crc, true);
    headerView.setUint32(18, data.length, true);
    headerView.setUint32(22, raw.length, true);
    headerView.setUint16(26, nameBytes.length, true);
    headerView.setUint16(28, localExtraLength, true);
    header.set(nameBytes, 30);
    header.fill(0xff, 30 + nameBytes.length);

    local.push(header, data);

    const entry = new Uint8Array(46 + nameBytes.length);
    const entryView = new DataView(entry.buffer);
    entryView.setUint32(0, 0x02014b50, true);
    entryView.setUint16(6, 20, true);
    entryView.setUint16(10, method, true);
    entryView.setUint32(16, crc, true);
    entryView.setUint32(20, data.length, true);
    entryView.setUint32(24, raw.length, true);
    entryView.setUint16(28, nameBytes.length, true);
    entryView.setUint32(42, offset, true);
    entry.set(nameBytes, 46);

    central.push(entry);
    offset += header.length + data.length;
  }

  const centralSize = central.reduce((total, e) => total + e.length, 0);
  const end = new Uint8Array(22);
  const endView = new DataView(end.buffer);
  endView.setUint32(0, 0x06054b50, true);
  endView.setUint16(8, entries.length, true);
  endView.setUint16(10, entries.length, true);
  endView.setUint32(12, centralSize, true);
  endView.setUint32(16, offset, true);

  return concat([...local, ...central, end]);
}

async function deflateRaw(data: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([data as BufferSource]).stream()
    .pipeThrough(new CompressionStream("deflate-raw"));

  return new Uint8Array(await new Response(stream).arrayBuffer());
}

function concat(parts: Uint8Array[]): Uint8Array {
  const total = parts.reduce((sum, part) => sum + part.length, 0);
  const out = new Uint8Array(total);
  let at = 0;

  for (const part of parts) {
    out.set(part, at);
    at += part.length;
  }

  return out;
}

function crc32(data: Uint8Array): number {
  let crc = 0xffffffff;

  for (const byte of data) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) {
      crc = crc & 1 ? (crc >>> 1) ^ 0xedb88320 : crc >>> 1;
    }
  }

  return (crc ^ 0xffffffff) >>> 0;
}
