#!/usr/bin/env -S deno run -A
/**
 * Stamp a product from `skeleton/` and build it, here rather than only in CI.
 *
 * Milestone 1 in `work/milestones.md`: `skeleton/` ships a complete product and
 * the only thing that has ever compiled it is CI's `skeleton-stamp` job. A
 * template nobody builds locally drifts, and the drift is found by whoever
 * stamps next -- the first defect this script found was every stamped product
 * dying at startup because `AddSignIn` registers the authorization services and
 * a product with no sign-in provider configured never calls it.
 *
 * Mixed resolution, the same combination CI uses and for the same reasons:
 * NativeWeb by CHECKOUT, because this working tree is the thing under test and
 * packages would test something published instead; JauntyQ by PACKAGES, because
 * there is no jauntyq sibling checkout here. Both are bare environment variables
 * rather than `-p:` flags -- MSBuild reads the environment as properties, so
 * they reach the child processes a `-p:` flag never would.
 *
 *   deno run -A tools/stamp-check.ts
 *   deno run -A tools/stamp-check.ts --with-database
 *   deno run -A tools/stamp-check.ts --name Widgets --slug widgets
 *   deno run -A tools/stamp-check.ts --packages
 *   deno run -A tools/stamp-check.ts --packages-from artifacts/packages
 *
 * `--packages` is milestone 6's check instead: NativeWeb by PACKAGES as well,
 * the pinned `Extrode.NativeWeb.*` versions restored from nuget.org into a cold
 * cache beside the destination (`<dest>.nuget`), with `NativeWebRoot` pointing
 * at nothing. A build alone would not prove it, because package mode is a switch
 * and a switch can be ignored; the run also fails unless every pinned
 * `Extrode.NativeWeb.*` id and version is in that cold cache afterwards. It
 * tests what is published, not this working tree, so it fails between a
 * VersionPrefix bump and that version's release.
 *
 * `--packages-from <dir>` is the same check against packages that are not
 * published yet: release.yml runs it on what it just packed, before the push, so
 * a package nuget.org would accept and no product could build from is caught
 * while its version number is still unspent. The stamped throwaway's
 * nuget.config gains `<dir>` as a source mapped to `Extrode.NativeWeb.*`, and the
 * run fails unless each pin's cache entry records that directory as its source.
 *
 * This script writes and never deletes. It refuses a destination that already
 * holds anything and names it, because removing someone's directory to make room
 * is not a decision a check should take.
 */

import { dirname, join, resolve } from "jsr:@std/path@1";

export interface Options {
  name: string;
  slug: string;
  destination: string | null;
  withDatabase: boolean;
  packages: boolean;
  packagesFrom: string | null;
}

export function parseArgs(argv: string[]): Options {
  const options: Options = {
    name: "Stampcheck",
    slug: "stampcheck",
    destination: null,
    withDatabase: false,
    packages: false,
    packagesFrom: null,
  };

  for (let i = 0; i < argv.length; i++) {
    const value = () => {
      if (i + 1 >= argv.length) throw new Error(`${argv[i]} needs a value.`);

      return argv[++i];
    };

    switch (argv[i]) {
      case "--name":
        options.name = value();
        break;
      case "--slug":
        options.slug = value();
        break;
      case "--dest":
        options.destination = value();
        break;
      case "--with-database":
        options.withDatabase = true;
        break;
      case "--packages":
        options.packages = true;
        break;
      case "--packages-from":
        options.packages = true;
        options.packagesFrom = value();
        break;
      default:
        throw new Error(`Unrecognised argument ${argv[i]}.`);
    }
  }

  // new-product.ps1 validates both and refuses on a mismatch; doing it here as
  // well is what makes the failure arrive before anything is written.
  if (!/^[A-Z][A-Za-z0-9]*$/.test(options.name)) {
    throw new Error(`--name must be PascalCase alphanumeric (got '${options.name}').`);
  }

  if (!/^[a-z][a-z0-9]*$/.test(options.slug)) {
    throw new Error(`--slug must be lowercase alphanumeric (got '${options.slug}').`);
  }

  return options;
}

/** The environment a stamped product needs to build against this working tree. */
export function stampEnvironment(repoRoot: string): Record<string, string> {
  return {
    NativeWebRoot: repoRoot,
    JauntyQUsePackages: "true",

    // "false", not blank and not omitted. `Deno.Command` merges the parent
    // environment rather than replacing it, and MSBuild reads every one of these
    // as a property, so a shell exporting NativeWebUsePackages=true would flip
    // this run to published packages while the output still said it was testing
    // this checkout. Blank is no longer enough either: package mode is the
    // skeleton's default for NativeWeb since 2026-11-04, as it is for JauntyQ
    // since 2026-09-05, so a blank resolves to packages. JauntyQRoot is blanked
    // so a shell exporting a root cannot make the resolved mode ambiguous.
    NativeWebUsePackages: "false",
    JauntyQRoot: "",

    DOTNET_NOLOGO: "1",
    DOTNET_CLI_TELEMETRY_OPTOUT: "1",
  };
}

/**
 * The environment for `--packages`: both frameworks from nuget.org, into a cache
 * nothing has restored into before.
 *
 * `NativeWebRoot` names a path under the cache that never exists, not a blank:
 * blank makes the skeleton probe for a `nativeweb` sibling of the product, and
 * a `--dest` beside a real checkout would find one.
 */
export function packageEnvironment(cacheRoot: string): Record<string, string> {
  return {
    NativeWebUsePackages: "true",
    NativeWebRoot: join(cacheRoot, "no-nativeweb-checkout"),
    JauntyQUsePackages: "true",
    JauntyQRoot: "",
    NUGET_PACKAGES: join(cacheRoot, "packages"),
    NUGET_HTTP_CACHE_PATH: join(cacheRoot, "http"),
    DOTNET_NOLOGO: "1",
    DOTNET_CLI_TELEMETRY_OPTOUT: "1",
  };
}

export interface Pin {
  id: string;
  version: string;
}

/** The `Extrode.NativeWeb.*` pins in a stamped product's Directory.Packages.props. */
export function nativeWebPins(props: string): Pin[] {
  const withoutComments = props.replace(/<!--[\s\S]*?-->/g, "");

  return [...withoutComments.matchAll(
    /<PackageVersion\s+Include="(Extrode\.NativeWeb\.[^"]+)"\s+Version="([^"]+)"/g,
  )].map((match) => ({ id: match[1], version: match[2] }));
}

/**
 * The pins a restore did not put in the cache from `source`, as `id version`, or
 * `id version (from <other>)` for one restored from somewhere else.
 *
 * NuGet writes `.nupkg.metadata` beside each package it extracts, and its
 * `source` field is the feed the package came from.
 */
export async function missingFromCache(
  pins: Pin[],
  packagesDir: string,
  source: string,
): Promise<string[]> {
  const missing: string[] = [];
  const expected = sourceKey(source);

  for (const pin of pins) {
    const metadata = join(packagesDir, pin.id.toLowerCase(), pin.version.toLowerCase(), ".nupkg.metadata");
    let actual: string | undefined;

    try {
      actual = JSON.parse(await Deno.readTextFile(metadata)).source;
    } catch (error) {
      if (!(error instanceof Deno.errors.NotFound)) throw error;
      missing.push(`${pin.id} ${pin.version}`);
      continue;
    }

    if (actual === undefined || sourceKey(actual) !== expected) {
      missing.push(`${pin.id} ${pin.version} (from ${actual ?? "an unrecorded source"})`);
    }
  }

  return missing;
}

/** A source as NuGet records it, compared without case or a trailing separator. */
function sourceKey(source: string): string {
  return source.replace(/[\\/]+$/, "").toLowerCase();
}

export const NUGET_ORG = "https://api.nuget.org/v3/index.json";

/**
 * The stamped product's nuget.config with `dir` added as a source that alone
 * serves `Extrode.NativeWeb.*`. NuGet's most specific pattern wins, so the
 * catch-all `*` on nuget.org still serves everything else, JauntyQ included.
 */
export function withLocalFeed(config: string, dir: string): string {
  const source = `    <add key="local-packages" value="${dir}" />\n`;
  const mapping = `    <packageSource key="local-packages">\n` +
    `      <package pattern="Extrode.NativeWeb.*" />\n` +
    `    </packageSource>\n`;

  if (!config.includes("</packageSources>") || !config.includes("</packageSourceMapping>")) {
    throw new Error("The stamped nuget.config has no packageSources and packageSourceMapping to extend.");
  }

  return config
    .replace("  </packageSources>", `${source}  </packageSources>`)
    .replace("  </packageSourceMapping>", `${mapping}  </packageSourceMapping>`);
}

/**
 * The stamp step's environment: the build's, plus permission to make the
 * stamped product's first commit.
 *
 * new-product.ps1 ends by committing the new product onto its own `main`, and a
 * global protected-branch hook sees that commit and refuses it -- the stamp then
 * fails with the files already written. The hook's own message names this
 * variable as the way to say the commit is intended, and it is: `main` is where
 * a new repository starts. It is scoped to this step because nothing the build
 * or the tests run commits anything.
 */
export function stampStepEnvironment(repoRoot: string): Record<string, string> {
  return { ...stampEnvironment(repoRoot), GIT_ALLOW_PROTECTED: "1" };
}

/**
 * Lines that mean the run failed, in MSBuild's diagnostic form.
 *
 * A warning counts. `TreatWarningsAsErrors` is on repo-wide, so a warning that
 * reaches a stamped product's build is a warning the framework let through, and
 * the whole point of this check is to see it here rather than in someone's new
 * repository. There is no opt-out; the fix is the warning.
 */
export function diagnosticsIn(output: string): string[] {
  return output
    .split(/\r?\n/)
    // The code is optional. An MSBuild <Warning Text="..."/> with no Code
    // prints "warning : ..." and C#-level TreatWarningsAsErrors does not
    // elevate an MSBuild task warning, so exit code stays 0 and requiring
    // [A-Z]+\d+ would let it through.
    .filter((line) => /\b(warning|error)\s*([A-Z]+\d+)?\s*:/i.test(line))
    .map((line) => line.trim());
}

/**
 * How many tests a `dotnet test` step actually ran. Null means its output
 * carries no summary line at all.
 *
 * Exit code is not enough -- or rather, it was not. Under VSTest a `--filter`
 * that matched nothing printed "No test matches the given testcase filter" and
 * exited 0 -- measured, not assumed -- so a renamed test or a drifted filter
 * reported the integration tier as `ok` having booted nothing. That is the one
 * outcome this whole script exists to prevent.
 *
 * Microsoft.Testing.Platform closed that hole itself: a run that finds no test
 * prints "Zero tests ran" and exits 8. The count is still read, because the
 * number is what the step reports to a human, and because the two guards
 * disagreeing is how a change in the platform's output would announce itself.
 *
 * The shape it reads is MTP's summary block rather than VSTest's one-liner:
 *
 *     Test run summary: Passed!
 *       total: 148
 *       failed: 0
 *       succeeded: 148
 *       skipped: 0
 */
export function testsRun(output: string): number | null {
  // The last block, not the first. One step runs one project today, but the
  // platform prints a block per test assembly before the aggregate, and reading
  // the first would report one assembly's count as the run's.
  const summaries = [...output.matchAll(/^\s*total:\s*(\d+)\s*$/gm)];

  if (summaries.length > 0) return Number(summaries.at(-1)![1]);

  return /Zero tests ran/.test(output) ? 0 : null;
}

/** What is already in the destination. Empty means it is safe to stamp into. */
export async function occupants(destination: string): Promise<string[]> {
  const found: string[] = [];

  try {
    for await (const entry of Deno.readDir(destination)) found.push(entry.name);
  } catch (error) {
    if (error instanceof Deno.errors.NotFound) return [];

    // Anything else -- a file where a directory was named, a permission refusal
    // -- is still "something is in the way". Rethrowing would escape the guard
    // below and lose the one thing this script promises: it removes nothing.
    return [`<${destination} could not be read as a directory: ${error}>`];
  }

  return found.sort();
}

/**
 * Everything in the stamped product's integration tier except the one fact that
 * opens a database connection. `Readyz_ReportsReady` is that fact; every other
 * test boots the host and asserts over HTTP, which needs nothing provisioned.
 */
export const DATABASE_FREE_FILTER = "FullyQualifiedName!~Readyz";

/**
 * The commands that make `--with-database` able to pass.
 *
 * All three lines are needed and an earlier version printed one. The stamped
 * tier resolves its connection string from `<Name>__Db__ConnectionString` and
 * falls back to `Database=<slug>_dev;Username=<slug>_app` with no password
 * (`skeleton/tests/__product__.tests.integration/TestDatabase.cs`), so creating
 * a database under any other name and stopping there leaves the run failing in
 * the same place it failed before. The names and the variable match the
 * `skeleton-stamp` job in ci.yml, which is the one configuration known to work.
 */
export function databaseHint(options: Pick<Options, "name" | "slug">): string[] {
  const connection = `Host=localhost;Database=${options.slug}_test;` +
    `Username=${options.slug}_app;Password=local_test_password`;

  return [
    `  psql -v ON_ERROR_STOP=1 \\`,
    `    -c "CREATE ROLE ${options.slug}_app LOGIN PASSWORD 'local_test_password';" \\`,
    `    -c "CREATE DATABASE ${options.slug}_test OWNER ${options.slug}_app;"`,
    `  export ${options.name}__Db__ConnectionString="${connection}"`,
    `  deno run -A tools/stamp-check.ts --with-database`,
  ];
}

export interface StepResult {
  label: string;
  code: number;
  output: string;
  seconds: number;
}

async function run(
  label: string,
  command: string,
  args: string[],
  cwd: string,
  env: Record<string, string>,
): Promise<StepResult> {
  const started = performance.now();

  const result = await new Deno.Command(command, {
    args,
    cwd,
    env,
    stdout: "piped",
    stderr: "piped",
  }).output();

  const decoder = new TextDecoder();

  return {
    label,
    code: result.code,
    output: decoder.decode(result.stdout) + decoder.decode(result.stderr),
    seconds: (performance.now() - started) / 1000,
  };
}

function repoRoot(): string {
  const path = decodeURIComponent(new URL(import.meta.url).pathname);

  return dirname(dirname(path.replace(/^\/([A-Za-z]:)/, "$1")));
}

async function main(argv: string[]): Promise<number> {
  const options = parseArgs(argv);
  const root = repoRoot();
  // Resolved, because the guard below runs in Deno.cwd() and new-product.ps1
  // runs with cwd: root. A relative --dest would be inspected in one directory
  // and stamped into another.
  const destination = options.destination === null
    ? join(root, "tmp", "stamp-check", options.slug)
    : resolve(options.destination);

  const cacheRoot = `${destination}.nuget`;
  const present = [
    ...await occupants(destination),
    ...(options.packages ? (await occupants(cacheRoot)).map((name) => `${cacheRoot}/${name}`) : []),
  ];

  if (present.length > 0) {
    const listed = present.slice(0, 5).join(", ") + (present.length > 5 ? ", ..." : "");

    console.error(
      `${destination} is not empty. It holds ${present.length}: ${listed}\n\n` +
        "A stamp needs an empty destination and this script deletes nothing. " +
        "Remove it yourself, or pass --dest somewhere else.",
    );

    return 1;
  }

  const env = options.packages ? packageEnvironment(cacheRoot) : stampEnvironment(root);
  const steps: StepResult[] = [];

  console.log(`Stamping ${options.name} (${options.slug}) into ${destination}`);

  steps.push(
    await run("stamp", "pwsh", [
      "-NoProfile",
      "-File",
      join(root, "tools", "new-product.ps1"),
      "-Name",
      options.name,
      "-Slug",
      options.slug,
      "-Destination",
      destination,
      "-Execute",
    ], root, stampStepEnvironment(root)),
  );

  const source = options.packagesFrom === null ? NUGET_ORG : resolve(options.packagesFrom);

  if (options.packagesFrom !== null && steps.at(-1)?.code === 0) {
    const config = join(destination, "nuget.config");

    await Deno.writeTextFile(config, withLocalFeed(await Deno.readTextFile(config), source));
  }

  if (steps[0].code === 0) {
    steps.push(
      await run("build", "dotnet", [
        "build",
        `${options.slug}.slnx`,
        "-v",
        "q",
        "--nologo",
      ], destination, env),
    );
  }

  if (options.packages && steps.at(-1)?.code === 0) {
    const started = performance.now();
    const pins = nativeWebPins(await Deno.readTextFile(join(destination, "Directory.Packages.props")));
    const missing = await missingFromCache(pins, env.NUGET_PACKAGES, source);
    const failure = pins.length === 0
      ? "The stamped Directory.Packages.props pins no Extrode.NativeWeb.* package."
      : `Not restored from ${source}, so the build used something else: ${missing.join(", ")}`;

    steps.push({
      label: `${pins.length} Extrode.NativeWeb.* pins restored from ${source}`,
      code: pins.length > 0 && missing.length === 0 ? 0 : 1,
      output: pins.length > 0 && missing.length === 0 ? "" : failure,
      seconds: (performance.now() - started) / 1000,
    });
  }

  if (steps.at(-1)?.code === 0) {
    steps.push(
      await run("unit tests", "dotnet", [
        "test",
        "--project",
        join("tests", `${options.slug}.tests.unit`),
        "--no-build",
        "-v",
        "q",
      ], destination, env),
    );
  }

  // The integration tier is what boots the stamped product's host, and booting
  // it is the whole reason this script exists: the defect that prompted it --
  // a product registering the authorization services only when a sign-in
  // provider was configured -- is invisible to a build and to the unit tier.
  //
  // Only one of its facts needs a database (readyz, which opens a connection),
  // and that database is a CI job step a workstation does not have. So the tier
  // runs either way and the default excludes that one fact by name, rather than
  // the tier being skipped and a green run meaning the host was never started.
  if (steps.at(-1)?.code === 0) {
    steps.push(
      await run(
        options.withDatabase ? "integration tests" : "integration tests (no database)",
        "dotnet",
        [
          "test",
          "--project",
          join("tests", `${options.slug}.tests.integration`),
          "--no-build",
          "-v",
          "q",
          ...(options.withDatabase ? [] : ["--filter", DATABASE_FREE_FILTER]),
        ],
        destination,
        env,
      ),
    );
  }

  console.log("");

  let failed = false;

  for (const step of steps) {
    const diagnostics = diagnosticsIn(step.output);
    const counts = step.label.includes("tests");
    const ran = counts ? testsRun(step.output) : null;
    const empty = ran === 0;
    // A tier whose count cannot be read fails. The regex reads one shape of one
    // platform's summary, so a reworded block or a quieter verbosity turns the
    // count guard off -- and silently, because exit 0 alone would still say ok.
    const unreadable = counts && ran === null;
    const ok = step.code === 0 && diagnostics.length === 0 && !empty && !unreadable;

    const count = ran === null || ran === 0 ? "" : `, ${ran} tests`;
    console.log(`  ${ok ? "ok  " : "FAIL"} ${step.label} (${step.seconds.toFixed(1)} s${count})`);

    if (ok) continue;

    failed = true;

    if (empty) {
      console.error(
        `  ${step.label} ran no tests and exited 0. A filter that matches nothing does that, ` +
          "so the tier was never executed and this run proves nothing.",
      );
    }

    if (unreadable) {
      console.error(
        `  ${step.label} exited ${step.code} and printed no count this script can read. ` +
          "Either the test platform's summary changed shape or the step ran something other " +
          "than a test run; the count guard is off until testsRun is re-taught.",
      );
    }

    console.error(step.output.trimEnd());

    for (const diagnostic of diagnostics) console.error(`  ${diagnostic}`);
  }

  // Only when the tier ran. Printed after a failed build it says a fact was
  // excluded when in truth no fact was attempted at all.
  const integrationRan = steps.some((step) => step.label.startsWith("integration tests"));

  if (!options.withDatabase && integrationRan) {
    console.log(`\nOne fact was not run: ${DATABASE_FREE_FILTER} excluded it.`);
    console.log("It opens the stamped product's database, which CI creates and a workstation");
    console.log("does not have. To run the whole tier:");
    console.log(databaseHint(options).join("\n"));
  }

  console.log(`\nThe stamped product is at ${destination}. Nothing here removes it.`);

  return failed ? 1 : 0;
}

if (import.meta.main) {
  try {
    Deno.exit(await main(Deno.args));
  } catch (error) {
    // The messages in parseArgs name what is wrong and what to write instead.
    // An unhandled rejection would replace every one of them with a stack trace
    // through Deno's internals.
    console.error(error instanceof Error ? error.message : String(error));
    Deno.exit(1);
  }
}
