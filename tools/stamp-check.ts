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

/**
 * The environment a stamped product needs to build against this working tree.
 *
 * GIT_ALLOW_PROTECTED is here because new-product.ps1 ends by committing the
 * stamped product onto its own `main`, and a global protected-branch hook sees
 * that commit and refuses it -- the stamp then fails with the files already
 * written. The hook's own message names this variable as the way to say the
 * commit is intended, and it is: `main` is where a new repository starts.
 */
export function stampEnvironment(repoRoot: string): Record<string, string> {
  return {
    NativeWebRoot: repoRoot,
    JauntyQUsePackages: "true",
    GIT_ALLOW_PROTECTED: "1",
    DOTNET_NOLOGO: "1",
    DOTNET_CLI_TELEMETRY_OPTOUT: "1",
  };
}

/** Lines that mean the run failed, in MSBuild's diagnostic form. */
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

    throw error;
  }

  return found.sort();
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
  const destination = options.destination ?? join(root, "tmp", "stamp-check", options.slug);

  const present = await occupants(destination);

  if (present.length > 0) {
    console.error(
      `${destination} already holds ${present.length} entr(y|ies): ${present.slice(0, 5).join(", ")}` +
        `${present.length > 5 ? ", ..." : ""}\n\n` +
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
    ], root, env),
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

  // The integration tier opens the stamped product's database, which CI creates
  // as a job step and a workstation does not have. Opt in once the role and the
  // database exist; the tier is not skipped silently, it is not run.
  if (options.withDatabase && steps.at(-1)?.code === 0) {
    steps.push(
      await run("integration tests", "dotnet", [
        "test",
        join("tests", `${options.slug}.tests.integration`),
        "--no-build",
        "-v",
        "q",
        "--nologo",
      ], destination, env),
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
    console.log("\nThe integration tier was not run: it needs the stamped product's database.");
    console.log(
      `  psql -c "CREATE ROLE ${options.slug}_app LOGIN PASSWORD '...';" ` +
        `-c "CREATE DATABASE ${options.slug}_test OWNER ${options.slug}_app;"`,
    );
    console.log("  then run again with --with-database.");
  }

  console.log(`\nThe stamped product is at ${destination}. Nothing here removes it.`);

  return failed ? 1 : 0;
}

if (import.meta.main) {
  Deno.exit(await main(Deno.args));
}
