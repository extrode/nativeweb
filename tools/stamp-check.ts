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
 *
 * This script writes and never deletes. It refuses a destination that already
 * holds anything and names it, because removing someone's directory to make room
 * is not a decision a check should take.
 */

import { dirname, join } from "jsr:@std/path@1";

export interface Options {
  name: string;
  slug: string;
  destination: string | null;
  withDatabase: boolean;
}

export function parseArgs(argv: string[]): Options {
  const options: Options = {
    name: "Stampcheck",
    slug: "stampcheck",
    destination: null,
    withDatabase: false,
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

    // Blanked, not omitted. `Deno.Command` merges the parent environment rather
    // than replacing it, and MSBuild reads every one of these as a property, so
    // a shell that exports NativeWebUsePackages=true would flip this run to
    // published packages while the output still said it was testing this
    // checkout. An empty value is false to `'$(X)' == 'true'`, which is the
    // form both props files test.
    NativeWebUsePackages: "",
    JauntyQRoot: "",

    DOTNET_NOLOGO: "1",
    DOTNET_CLI_TELEMETRY_OPTOUT: "1",
  };
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
    .filter((line) => /\b(warning|error)\s+[A-Z]+\d+\s*:/i.test(line))
    .map((line) => line.trim());
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
  const destination = options.destination ?? join(root, "tmp", "stamp-check", options.slug);

  const present = await occupants(destination);

  if (present.length > 0) {
    const listed = present.slice(0, 5).join(", ") + (present.length > 5 ? ", ..." : "");

    console.error(
      `${destination} is not empty. It holds ${present.length}: ${listed}\n\n` +
        "A stamp needs an empty destination and this script deletes nothing. " +
        "Remove it yourself, or pass --dest somewhere else.",
    );

    return 1;
  }

  const env = stampEnvironment(root);
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

  if (steps.at(-1)?.code === 0) {
    steps.push(
      await run("unit tests", "dotnet", [
        "test",
        join("tests", `${options.slug}.tests.unit`),
        "--no-build",
        "-v",
        "q",
        "--nologo",
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
          join("tests", `${options.slug}.tests.integration`),
          "--no-build",
          "-v",
          "q",
          "--nologo",
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
    const ok = step.code === 0 && diagnostics.length === 0;

    console.log(`  ${ok ? "ok  " : "FAIL"} ${step.label} (${step.seconds.toFixed(1)} s)`);

    if (ok) continue;

    failed = true;
    console.error(step.output.trimEnd());

    for (const diagnostic of diagnostics) console.error(`  ${diagnostic}`);
  }

  if (!options.withDatabase) {
    console.log(`\nOne fact was not run: ${DATABASE_FREE_FILTER} excluded it.`);
    console.log("It opens the stamped product's database, which CI creates and a workstation");
    console.log("does not have. To run the whole tier:");
    console.log(
      `  psql -c "CREATE ROLE ${options.slug}_app LOGIN PASSWORD '...';" ` +
        `-c "CREATE DATABASE ${options.slug}_test OWNER ${options.slug}_app;"`,
    );
    console.log("  deno run -A tools/stamp-check.ts --with-database");
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
