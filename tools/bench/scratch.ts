// Test scratch directories, repo-local.
//
// `Deno.makeTempDir()` with no `dir` lands in the OS temp directory, which every project on the
// machine shares and none owns. Under the repo's own `tmp/` instead, so a killed test run
// leaves its debris where the project's cleanup script can see it. Same rule as
// tools/audit/scripts.test.ts.

import { join } from "@std/path";

import { repoRoot } from "./run.ts";

/** A fresh empty directory under the repo's `tmp/`, and the call that removes it again. */
export async function scratchDir(prefix = "bench-test-"): Promise<string> {
  const parent = join(repoRoot(), "tmp");
  await Deno.mkdir(parent, { recursive: true });
  return await Deno.makeTempDir({ dir: parent, prefix });
}

export async function removeScratch(dir: string): Promise<void> {
  try {
    await Deno.remove(dir, { recursive: true });
  } catch {
    // A test that already removed it, or a file still held open on Windows; the directory is
    // under tmp/ either way and the cleanup script names it.
  }
}
