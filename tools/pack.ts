#!/usr/bin/env -S deno run -A
/**
 * Pack every packable NativeWeb library, then read back what was produced.
 *
 * Spec 028 R5. This is the one pack command: a person runs it locally, CI runs
 * it unchanged on every push, and the publish workflow runs it with --release.
 *
 * The verification half is the point. `dotnet pack` succeeding says the build
 * worked; it says nothing about whether the package carries its version, its
 * provenance or its README, and each of those is a fact that exists only inside
 * the file that was produced. So this opens each .nupkg and asserts against the
 * nuspec rather than against the MSBuild properties that were meant to produce
 * it -- a property set in the wrong conditional block still reads correctly in
 * the file it was written in.
 *
 * Usage:
 *   deno run -A tools/pack.ts [--release] [--out <dir>] [extra dotnet args...]
 *
 * Any argument this script does not recognise is forwarded to `dotnet pack`,
 * which is how package mode is selected: `-p:JauntyQUsePackages=true`.
 */

import { dirname, join } from "jsr:@std/path@1";

// ---------------------------------------------------------------------------
// Pure helpers. Exported for tools/pack.test.ts, which covers them without
// invoking dotnet -- the parsing is where this script can be wrong quietly.
// ---------------------------------------------------------------------------

/** The single `<VersionPrefix>` in Directory.Build.props (spec 028 R1). */
export function versionPrefixOf(propsXml: string): string {
  const matches = [...propsXml.matchAll(/<VersionPrefix>([^<]+)<\/VersionPrefix>/g)];

  if (matches.length !== 1) {
    throw new Error(
      `Expected exactly one <VersionPrefix> in Directory.Build.props, found ${matches.length}. ` +
        "It is the source of truth for every NativeWeb.* package version.",
    );
  }

  return matches[0][1].trim();
}

/** What a package's version should read, given the release flag. */
export function expectedVersion(prefix: string, release: boolean): string {
  return release ? prefix : `${prefix}-dev`;
}

export function isPackable(csprojXml: string): boolean {
  return /<IsPackable>\s*true\s*<\/IsPackable>/.test(csprojXml);
}

export function packageIdOf(csprojXml: string): string | null {
  return csprojXml.match(/<PackageId>([^<]+)<\/PackageId>/)?.[1].trim() ?? null;
}

/**
 * The lines in pack output that mean something went wrong.
 *
 * Deliberately narrower than "contains the word warning". Two things in a
 * normal pack log carry the word and mean nothing: MSBuild's own `0 Warning(s)`
 * summary, and any project or output path with `error` or `warning` in it --
 * `libs/nativeweb.web/ErrorPageMiddleware.cs` is in this repository today.
 * The shape matched here is MSBuild's diagnostic form, `warning NU5039:` and
 * `error CS0246:`, which is what a line that means something looks like.
 *
 * NuGet's advice for a package with no README is a third case and needs no
 * special handling: it is plain prose carrying neither word, and the four
 * generator packages legitimately have no README.
 */
export function diagnosticsIn(output: string): string[] {
  return output
    .split(/\r?\n/)
    .filter((line) => /\b(warning|error)\s+[A-Z]+\d+\s*:/i.test(line))
    .map((line) => line.trim());
}

export interface NuspecFacts {
  id: string;
  version: string;
  readme: string | null;
  repositoryUrl: string | null;
  repositoryCommit: string | null;
}

export function nuspecFacts(nuspecXml: string): NuspecFacts {
  const one = (tag: string) =>
    nuspecXml.match(new RegExp(`<${tag}>([^<]*)</${tag}>`))?.[1].trim() ?? null;

  const repository = nuspecXml.match(/<repository\b[^>]*\/?>/)?.[0] ?? "";
  const attribute = (name: string) =>
    repository.match(new RegExp(`${name}="([^"]*)"`))?.[1] ?? null;

  const id = one("id");
  const version = one("version");

  if (id === null || version === null) {
    throw new Error("nuspec carries no <id> or no <version>.");
  }

  return {
    id,
    version,
    readme: one("readme"),
    repositoryUrl: attribute("url"),
    repositoryCommit: attribute("commit"),
  };
}

// ---------------------------------------------------------------------------
// Reading a .nupkg.
//
// A .nupkg is a zip, and Deno ships no zip reader -- but it ships
// DecompressionStream("deflate-raw"), which is the only hard part. Everything
// below is the central-directory walk around it. Shelling out to `unzip` was
// the alternative and was rejected: it is absent from a default Windows install
// and this script has to run on both sides of the same CI matrix.
//
// No zip64 handling. A .nupkg over 4 GB would need it; the largest here is
// under a megabyte, and the parse throws rather than misreading if that ever
// stops being true.
// ---------------------------------------------------------------------------

const EOCD_SIGNATURE = 0x06054b50;
const CENTRAL_SIGNATURE = 0x02014b50;

export async function readZip(bytes: Uint8Array): Promise<Map<string, Uint8Array>> {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  let eocd = -1;
  // The end-of-central-directory record sits at the end, after a comment of up
  // to 65535 bytes, so it is found by scanning backwards rather than by offset.
  for (let i = bytes.length - 22; i >= 0; i--) {
    if (view.getUint32(i, true) === EOCD_SIGNATURE) {
      eocd = i;
      break;
    }
  }

  if (eocd < 0) {
    throw new Error("Not a zip: no end-of-central-directory record.");
  }

  const entryCount = view.getUint16(eocd + 10, true);
  let cursor = view.getUint32(eocd + 16, true);

  if (cursor === 0xffffffff || entryCount === 0xffff) {
    throw new Error("Zip64 archive; this reader does not handle one.");
  }

  const files = new Map<string, Uint8Array>();

  for (let entry = 0; entry < entryCount; entry++) {
    if (view.getUint32(cursor, true) !== CENTRAL_SIGNATURE) {
      throw new Error(`Central directory entry ${entry} has the wrong signature.`);
    }

    const method = view.getUint16(cursor + 10, true);
    const compressedSize = view.getUint32(cursor + 20, true);
    const nameLength = view.getUint16(cursor + 28, true);
    const extraLength = view.getUint16(cursor + 30, true);
    const commentLength = view.getUint16(cursor + 32, true);
    const localOffset = view.getUint32(cursor + 42, true);

    const name = new TextDecoder().decode(
      bytes.subarray(cursor + 46, cursor + 46 + nameLength),
    );

    // The local header repeats the name and carries its own extra field, whose
    // length differs from the central one, so the data offset has to be read
    // from the local header rather than computed from the central entry.
    const localNameLength = view.getUint16(localOffset + 26, true);
    const localExtraLength = view.getUint16(localOffset + 28, true);
    const dataStart = localOffset + 30 + localNameLength + localExtraLength;
    const data = bytes.subarray(dataStart, dataStart + compressedSize);

    files.set(name, method === 0 ? data : await inflateRaw(data));

    cursor += 46 + nameLength + extraLength + commentLength;
  }

  return files;
}

async function inflateRaw(data: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([data as BufferSource]).stream()
    .pipeThrough(new DecompressionStream("deflate-raw"));

  return new Uint8Array(await new Response(stream).arrayBuffer());
}

// ---------------------------------------------------------------------------
// The run.
// ---------------------------------------------------------------------------

export interface PackableProject {
  csproj: string;
  directory: string;
  packageId: string;
  hasReadme: boolean;
}

export async function packableProjects(libs: string): Promise<PackableProject[]> {
  const found: PackableProject[] = [];

  for await (const entry of Deno.readDir(libs)) {
    if (!entry.isDirectory) continue;

    const directory = join(libs, entry.name);

    for await (const file of Deno.readDir(directory)) {
      if (!file.name.endsWith(".csproj")) continue;

      const csproj = join(directory, file.name);
      const xml = await Deno.readTextFile(csproj);

      if (!isPackable(xml)) continue;

      const packageId = packageIdOf(xml);

      if (packageId === null) {
        throw new Error(`${csproj} sets IsPackable but declares no PackageId.`);
      }

      found.push({
        csproj,
        directory,
        packageId,
        hasReadme: await exists(join(directory, "README.md")),
      });
    }
  }

  return found.sort((a, b) => a.packageId.localeCompare(b.packageId));
}

async function exists(path: string): Promise<boolean> {
  try {
    await Deno.stat(path);
    return true;
  } catch {
    return false;
  }
}

export interface Options {
  release: boolean;
  out: string | null;
  forwarded: string[];
}

/**
 * Everything this script does not recognise goes to `dotnet pack` untouched.
 *
 * That is how package mode is selected (`-p:JauntyQUsePackages=true`), and it
 * is why the two flags are consumed by index rather than filtered by value: a
 * forwarded argument could legitimately be the string "--out", and dropping
 * every occurrence would silently change what dotnet was asked to do.
 */
export function parseArgs(argv: string[]): Options {
  const options: Options = { release: false, out: null, forwarded: [] };

  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--release") {
      options.release = true;
    } else if (argv[i] === "--out") {
      if (i + 1 >= argv.length) throw new Error("--out needs a directory.");
      options.out = argv[++i];
    } else {
      options.forwarded.push(argv[i]);
    }
  }

  return options;
}

function repoRoot(): string {
  // tools/pack.ts -> tools -> repo root. Resolved from this file rather than
  // from the cwd so the script works from anywhere, which is what lets a
  // workflow step call it without a `cd`.
  return dirname(dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")));
}

async function main(argv: string[]): Promise<number> {
  const { release, out: requestedOut, forwarded } = parseArgs(argv);

  const root = repoRoot();
  const out = requestedOut ?? join(root, "artifacts", "packages");

  const prefix = versionPrefixOf(await Deno.readTextFile(join(root, "Directory.Build.props")));
  const version = expectedVersion(prefix, release);
  const projects = await packableProjects(join(root, "libs"));

  console.log(
    `Packing ${projects.length} project(s) as ${version}` +
      `${release ? " (RELEASE)" : ""} into ${out}`,
  );

  await Deno.mkdir(out, { recursive: true });

  const failures: string[] = [];

  for (const project of projects) {
    const args = [
      "pack",
      project.csproj,
      "-c",
      "Release",
      "-o",
      out,
      "--nologo",
      ...(release ? ["-p:IsReleaseBuild=true"] : []),
      ...forwarded,
    ];

    const result = await new Deno.Command("dotnet", {
      args,
      stdout: "piped",
      stderr: "piped",
    }).output();

    const output = new TextDecoder().decode(result.stdout) +
      new TextDecoder().decode(result.stderr);

    if (result.code !== 0) {
      failures.push(`${project.packageId}: dotnet pack exited ${result.code}\n${output}`);
      continue;
    }

    const diagnostics = diagnosticsIn(output);

    if (diagnostics.length > 0) {
      failures.push(`${project.packageId}: pack emitted diagnostics\n  ${diagnostics.join("\n  ")}`);
      continue;
    }

    const problem = await verify(project, join(out, `${project.packageId}.${version}.nupkg`), version);

    if (problem !== null) {
      failures.push(`${project.packageId}: ${problem}`);
      continue;
    }

    console.log(`  ok  ${project.packageId}.${version}.nupkg`);
  }

  if (failures.length > 0) {
    console.error(`\n${failures.length} of ${projects.length} package(s) failed:\n`);
    for (const failure of failures) console.error(`- ${failure}\n`);
    return 1;
  }

  console.log(`\nAll ${projects.length} packages produced and verified.`);
  return 0;
}

/** Everything spec 028 asks a produced package to carry. Null means it does. */
export async function verify(
  project: PackableProject,
  nupkg: string,
  version: string,
): Promise<string | null> {
  let bytes: Uint8Array;

  try {
    bytes = await Deno.readFile(nupkg);
  } catch {
    return `pack reported success but ${nupkg} does not exist.`;
  }

  const files = await readZip(bytes);
  const nuspecName = `${project.packageId}.nuspec`;
  const nuspecBytes = files.get(nuspecName);

  if (nuspecBytes === undefined) {
    return `package contains no ${nuspecName}.`;
  }

  const facts = nuspecFacts(new TextDecoder().decode(nuspecBytes));

  if (facts.version !== version) {
    return `nuspec version is ${facts.version}, expected ${version} (R1, R2).`;
  }

  if (facts.repositoryUrl === null || facts.repositoryCommit === null) {
    return "nuspec <repository> is missing url or commit (R3): " +
      `url=${facts.repositoryUrl}, commit=${facts.repositoryCommit}.`;
  }

  // ISL-NETC 3.1(c): a copy of the software retains the notice, and a .nupkg
  // is a copy. Directory.Build.targets packs both; this is what proves it did.
  for (const required of ["LICENSE.md", "NOTICE.md"]) {
    if (!files.has(required)) return `package contains no ${required}.`;
  }

  if (project.hasReadme) {
    if (facts.readme !== "README.md") {
      return `the project has a README.md and the nuspec names ${facts.readme} (R4).`;
    }

    if (!files.has("README.md")) {
      return "the nuspec names README.md and the package does not contain it (R4).";
    }
  } else if (facts.readme !== null) {
    return `the project has no README.md and the nuspec names ${facts.readme} (R4).`;
  }

  return null;
}

if (import.meta.main) {
  Deno.exit(await main(Deno.args));
}
