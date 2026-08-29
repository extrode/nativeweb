// The two shell scripts are the only part of this spec that touches a machine, so what is
// testable about them is what they refuse to do: no side effect without --execute, no push to
// origin, no removal of anything. The rest — that launchd loads, that the host holds one
// credential — is a property of audit-host after the user has run the provisioner, and no test here can
// assert it.

import { assert, assertEquals, assertStringIncludes } from "@std/assert";
import { fromFileUrl, join } from "@std/path";

const REPO = fromFileUrl(new URL("../..", import.meta.url));
const SYNC = join(REPO, "scripts", "audit", "sync-to-audit-host.sh");
const PROVISION = join(REPO, "scripts", "audit", "provision-audit-host.sh");

async function bash(args: string[]): Promise<{ code: number; out: string; err: string }> {
  const { code, stdout, stderr } = await new Deno.Command("bash", {
    args,
    cwd: REPO,
    stdout: "piped",
    stderr: "piped",
  }).output();
  const dec = new TextDecoder();
  return { code, out: dec.decode(stdout), err: dec.decode(stderr) };
}

async function read(path: string): Promise<string> {
  return await Deno.readTextFile(path);
}

/**
 * Run a script with `ssh` replaced by a shim that records the attempt and fails.
 *
 * Asserting "the dry run exits 0" is not enough, and this repository has the measurement to prove
 * it: removing the dry run's early `exit 0` left every assertion green while the script went on
 * to mirror the whole repository to the audit host over the network. A dry run has to be shown
 * not to have *tried*, not merely to have succeeded.
 */
async function bashWithoutSsh(
  args: string[],
): Promise<{ code: number; out: string; err: string; sshAttempts: string[] }> {
  const shimDir = await Deno.makeTempDir({ dir: join(REPO, "tmp"), prefix: "ssh-shim-" });
  const marker = join(shimDir, "attempts.log");
  const shim = join(shimDir, "ssh");
  await Deno.writeTextFile(
    shim,
    `#!/usr/bin/env bash\nprintf '%s\\n' "$*" >> ${JSON.stringify(marker)}\nexit 97\n`,
  );
  await Deno.chmod(shim, 0o755).catch(() => {}); // no-op on Windows, required elsewhere

  try {
    const { code, stdout, stderr } = await new Deno.Command("bash", {
      args,
      cwd: REPO,
      env: {
        PATH: `${shimDir}:${Deno.env.get("PATH") ?? ""}`,
        GIT_SSH_COMMAND: shim.replaceAll("\\", "/"),
      },
      stdout: "piped",
      stderr: "piped",
    }).output();
    const dec = new TextDecoder();
    let attempts: string[] = [];
    try {
      attempts = (await Deno.readTextFile(marker)).split("\n").filter((l) => l.length > 0);
    } catch {
      // No marker: nothing invoked the shim, which is the outcome these tests want.
    }
    return { code, out: dec.decode(stdout), err: dec.decode(stderr), sshAttempts: attempts };
  } finally {
    await Deno.remove(shimDir, { recursive: true });
  }
}

Deno.test("sync-to-audit-host.sh parses as bash", async () => {
  assertEquals((await bash(["-n", SYNC])).code, 0);
});

Deno.test("provision-audit-host.sh parses as bash", async () => {
  assertEquals((await bash(["-n", PROVISION])).code, 0);
});

Deno.test("a bare sync invocation is a dry run that reaches no host at all", async () => {
  const before = (await bash(["-c", "git status --porcelain"])).out;
  const result = await bashWithoutSsh([SYNC]);
  assertEquals(result.code, 0);
  assertStringIncludes(result.out, "Dry run");
  assertStringIncludes(result.out, "Nothing was pushed");
  assertEquals(result.sshAttempts, []);
  assertEquals((await bash(["-c", "git status --porcelain"])).out, before);
});

Deno.test("a bare provision invocation reaches no host either", async () => {
  // On Windows this would pass vacuously — the script exits 2 at the Darwin guard long before
  // the reachability probe — so the guard is patched out and a fabricated HOME supplied, the
  // same way the credential tests do it. Otherwise the assertion proves nothing on the machine
  // it runs on and nothing about the machine it is written for.
  const home = await fakeHome(false);
  try {
    const result = await provisionWithHome(home, [], { recordSsh: true });
    assertEquals(result.code, 0);
    assertEquals(result.sshAttempts, []);
    assertStringIncludes(result.out, "a dry run makes no outbound connection");
  } finally {
    await Deno.remove(home, { recursive: true });
  }
});

Deno.test("the dry run names the mirror it would push to, and it is not origin", async () => {
  const result = await bash([SYNC]);
  assertStringIncludes(result.out, "audit/mirror/nativeweb.git");
  assert(!/origin/.test(result.out));
});

Deno.test("sync has no code path that pushes to origin", async () => {
  const text = await read(SYNC);
  const pushes = text.split("\n").filter((l) => /git push/.test(l) && !l.trimStart().startsWith("#"));
  assertEquals(pushes.length, 1);
  assertStringIncludes(pushes[0], '"$MIRROR"');
  assert(!/origin/.test(pushes[0]));
});

Deno.test("an unknown flag is refused rather than ignored", async () => {
  for (const path of [SYNC, PROVISION]) {
    const result = await bash([path, "--force"]);
    assertEquals(result.code, 2, `${path} accepted --force`);
    assertStringIncludes(result.err, "unknown argument");
  }
});

Deno.test("both scripts use the fixed cleanup-style interface: bare is the dry run", async () => {
  for (const path of [SYNC, PROVISION]) {
    const text = await read(path);
    assertStringIncludes(text, "--execute|-e");
    assertStringIncludes(text, "EXECUTE=0");
  }
});

Deno.test("provisioning refuses to run anywhere but macOS", async () => {
  // Run from Git Bash on Windows, `uname -s` is MINGW64_NT-*, so this is the live path here.
  const result = await bash([PROVISION]);
  assertEquals(result.code, 2);
  assertStringIncludes(result.out, "Refusing");
});

Deno.test("neither script removes anything, per the never-clean-up rule", async () => {
  for (const path of [SYNC, PROVISION]) {
    const lines = (await read(path)).split("\n")
      .filter((l) => !l.trimStart().startsWith("#"))
      .filter((l) => /\brm\b|\brmdir\b|--delete\b/.test(l));
    assertEquals(lines, [], `${path} removes something`);
  }
});

Deno.test("the provisioner reports a found credential rather than deleting it", async () => {
  const text = await read(PROVISION);
  assertStringIncludes(text, "R15 says this host holds no production credential");
  assertStringIncludes(text, "Reported, never removed");
});

/**
 * Run the provisioner against a fabricated HOME, with its macOS guard patched out.
 *
 * The guard is what makes the script safe to hand someone, and patching it in a test is not a
 * backdoor in the script — the copy under test is a temporary file the test wrote. Without this
 * the credential path is unreachable from a Windows workstation, and an assertion that merely
 * greps for `exit 3` passes when the code says `exit 0`, because the usage text mentions it too.
 * That is measured: two mutations survived a string-matching version of this test.
 */
async function provisionWithHome(
  home: string,
  extraArgs: string[] = [],
  opts: { recordSsh?: boolean } = {},
): Promise<{ code: number; out: string; sshAttempts: string[] }> {
  const dir = await Deno.makeTempDir({ dir: join(REPO, "tmp"), prefix: "provision-" });
  try {
    const patched = (await read(PROVISION)).replace(
      'if [ "$(uname -s)" != "Darwin" ]; then',
      "if false; then",
    );
    const script = join(dir, "provision.sh");
    await Deno.writeTextFile(script, patched);
    const marker = join(dir, "attempts.log");
    const shim = join(dir, "ssh");
    await Deno.writeTextFile(
      shim,
      `#!/usr/bin/env bash\nprintf '%s\\n' "$*" >> ${JSON.stringify(marker)}\nexit 97\n`,
    );
    await Deno.chmod(shim, 0o755).catch(() => {});

    const { code, stdout, stderr } = await new Deno.Command("bash", {
      args: [script, ...extraArgs],
      cwd: REPO,
      env: {
        HOME: home,
        PATH: `${dir}:${Deno.env.get("PATH") ?? ""}`,
        AUDIT_HOME: join(home, "audit"),
        DENO_INSTALL: join(home, ".deno"),
      },
      stdout: "piped",
      stderr: "piped",
    }).output();
    const dec = new TextDecoder();
    let sshAttempts: string[] = [];
    if (opts.recordSsh === true) {
      try {
        sshAttempts = (await Deno.readTextFile(marker)).split("\n").filter((l) => l.length > 0);
      } catch {
        // No marker: the shim was never invoked, which is what the caller is asserting.
      }
    }
    return { code, out: dec.decode(stdout) + dec.decode(stderr), sshAttempts };
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
}

async function fakeHome(withCredential: boolean): Promise<string> {
  const home = await Deno.makeTempDir({ dir: join(REPO, "tmp"), prefix: "home-" });
  if (withCredential) await Deno.writeTextFile(join(home, ".pgpass"), "host:5432:db:user:secret\n");
  return home;
}

Deno.test("a production credential on the host refuses the run, exit 3", async () => {
  const home = await fakeHome(true);
  try {
    const result = await provisionWithHome(home);
    assertEquals(result.code, 3);
    assertStringIncludes(result.out, "Refusing to provision");
    assertStringIncludes(result.out, ".pgpass");
    // Refused before anything was installed or created.
    assert(!result.out.includes("Scheduler (R2)"));
  } finally {
    await Deno.remove(home, { recursive: true });
  }
});

Deno.test("a clean host proceeds to the dry run", async () => {
  const home = await fakeHome(false);
  try {
    const result = await provisionWithHome(home);
    assertEquals(result.code, 0);
    assertStringIncludes(result.out, "no production credential found");
    assertStringIncludes(result.out, "Dry run. Nothing was installed");
  } finally {
    await Deno.remove(home, { recursive: true });
  }
});

Deno.test("--accept-credential-risk proceeds, and says it is doing so", async () => {
  const home = await fakeHome(true);
  try {
    const result = await provisionWithHome(home, ["--accept-credential-risk"]);
    assertEquals(result.code, 0);
    assertStringIncludes(result.out, "--accept-credential-risk was given");
    assertStringIncludes(result.out, "Scheduler (R2)");
  } finally {
    await Deno.remove(home, { recursive: true });
  }
});

Deno.test("the risk opt-out is named for what it does, not --force", async () => {
  // The fixed cleanup interface: anything irreversible past --execute needs a second flag named
  // for the thing it permits.
  const text = await read(PROVISION);
  assertStringIncludes(text, "--accept-credential-risk");
  assert(!/--force\b/.test(text));
});

Deno.test("a dry run creates nothing under the fabricated home", async () => {
  const home = await fakeHome(false);
  try {
    await provisionWithHome(home);
    const entries: string[] = [];
    for await (const e of Deno.readDir(home)) entries.push(e.name);
    assertEquals(entries, []);
  } finally {
    await Deno.remove(home, { recursive: true });
  }
});

Deno.test("the credential and reachability checks run before anything is installed", async () => {
  const text = await read(PROVISION);
  assert(
    text.indexOf("1. Credentials (R15)") < text.indexOf("install Deno to"),
    "R15 is checked after Deno is installed, so a refusal would leave the host changed",
  );
});

Deno.test("the provisioner checks whether the audit host can reach origin", async () => {
  // R1 is structural only while audit-host cannot authenticate to github.com. If it can, that has to be
  // said out loud rather than assumed away.
  const text = await read(PROVISION);
  assertStringIncludes(text, "git@github.com");
  assertStringIncludes(text, "R1's structural guarantee is weakened");
});

Deno.test("the launchd agent runs at load, so a missed day is not silently skipped", async () => {
  const text = await read(PROVISION);
  assertStringIncludes(text, "<key>RunAtLoad</key><true/>");
  assertStringIncludes(text, "StartCalendarInterval");
});

Deno.test("the launchd agent invokes deno by absolute path, not through PATH", async () => {
  // A launchd agent gets a minimal environment; a bare `deno` is how these fail silently.
  const text = await read(PROVISION);
  assertStringIncludes(text, "<string>${DENO_DIR}/bin/deno</string>");
});

Deno.test("provisioning installs no Node toolchain", async () => {
  // `~/.npmrc` appears in the credential list and `npm` in the comment explaining the rule; what
  // must not appear is an invocation. Matching the word anywhere would ban naming the thing.
  for (const path of [SYNC, PROVISION]) {
    const offenders = (await read(path)).split("\n")
      .filter((l) => !l.trimStart().startsWith("#"))
      .filter((l) => /(^|[;&|(]\s*)(npm|npx|node)\s/.test(l) || /node_modules/.test(l));
    assertEquals(offenders, [], `${path} invokes Node tooling`);
  }
});
