import { assertEquals, assertRejects, assertThrows } from "jsr:@std/assert@1";

import {
  audit,
  failures,
  isRemovedPath,
  loadRules,
  parseArgs,
  parsePathList,
  parseRules,
  type Rules,
} from "./audit-scrubbed-history.ts";

const RULES: Rules = {
  paths: ["apps/secret", "docs/host.md"],
  text: parseRules("regex:(?<!Jane )Acme==>Legacy\nregex:\\bhost7\\b==>web-host\nliteral:10.9.8.7==>10.0.0.7"),
  message: parseRules("regex:(?<!Jane )Acme==>Legacy\nregex:\\bhangar\\b==>host"),
};

async function run(dir: string, ...args: string[]) {
  const out = await new Deno.Command("git", { args: ["-C", dir, ...args], stdout: "null", stderr: "piped" })
    .output();
  if (!out.success) throw new Error(new TextDecoder().decode(out.stderr));
}

async function fixture(
  commits: { files: Record<string, string | null>; message: string; author?: string }[],
): Promise<string> {
  const dir = await Deno.makeTempDir({ prefix: "audit-fixture-" });
  await run(dir, "init", "-q", "-b", "work");
  await run(dir, "config", "core.hooksPath", ".git/no-hooks");
  for (const c of commits) {
    for (const [path, content] of Object.entries(c.files)) {
      if (content === null) {
        await run(dir, "rm", "-q", path);
        continue;
      }
      await Deno.mkdir(`${dir}/${path}`.replace(/\/[^/]+$/, ""), { recursive: true });
      await Deno.writeTextFile(`${dir}/${path}`, content);
      await run(dir, "add", path);
    }
    const [name, email] = (c.author ?? "Jane Acme <jane@example.test>").match(/^(.*) <(.*)>$/)!.slice(1);
    await run(dir, "-c", `user.name=${name}`, "-c", `user.email=${email}`, "commit", "-q", "-m", c.message);
  }
  return dir;
}

async function withFixture(
  commits: Parameters<typeof fixture>[0],
  body: (dir: string) => Promise<void>,
) {
  const dir = await fixture(commits);
  try {
    await body(dir);
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
}

Deno.test("a history that never held a removed path or string is clean", async () => {
  await withFixture([
    { files: { "README.md": "Written by Jane Acme for anyone." }, message: "docs: readme" },
    { files: { "src/app.cs": "class App {}" }, message: "feat: app" },
  ], async (dir) => {
    const report = await audit(dir, RULES, "work");
    assertEquals(failures(report), 0);
  });
});

Deno.test("a string removed at the tip still fails the history, not the tip tree", async () => {
  await withFixture([
    { files: { "docs/deploy.md": "deploy to host7 at 10.9.8.7" }, message: "docs: deploy" },
    { files: { "docs/deploy.md": "deploy to your host" }, message: "docs: generic deploy" },
  ], async (dir) => {
    const history = await audit(dir, RULES, "work");
    assertEquals(history.blobHits.map((h) => h.rule).sort(), ["10.9.8.7", "\\bhost7\\b"]);
    assertEquals(failures(await audit(dir, RULES, "work", true)), 0);
  });
});

Deno.test("a removed path anywhere in history fails, prefix or exact", async () => {
  await withFixture([
    { files: { "apps/secret/Login.cs": "x", "docs/host.md": "y", "apps/secretive/ok.cs": "z" }, message: "add" },
    { files: { "apps/secret/Login.cs": null, "docs/host.md": null }, message: "remove" },
  ], async (dir) => {
    const report = await audit(dir, RULES, "work");
    assertEquals(report.pathHits, ["apps/secret/Login.cs", "docs/host.md"]);
  });
});

Deno.test("a removed string in a path name fails, even with clean content", async () => {
  await withFixture([
    { files: { "scripts/move-to-host7.ps1": "x" }, message: "add" },
    { files: { "scripts/move-to-host7.ps1": null }, message: "remove" },
  ], async (dir) => {
    const report = await audit(dir, RULES, "work");
    assertEquals(report.pathHits, ["scripts/move-to-host7.ps1"]);
    assertEquals(report.blobHits.length, 0);
  });
});

Deno.test("a removed path introduced only by a merge commit fails", async () => {
  await withFixture([{ files: { "README.md": "r" }, message: "docs: readme" }], async (dir) => {
    await run(dir, "switch", "-q", "-c", "side");
    await Deno.writeTextFile(`${dir}/side.md`, "s");
    await run(dir, "add", "side.md");
    await run(dir, "-c", "user.name=Jane Acme", "-c", "user.email=jane@example.test", "commit", "-q", "-m", "side");
    await run(dir, "switch", "-q", "work");
    await run(dir, "-c", "user.name=Jane Acme", "-c", "user.email=jane@example.test", "merge", "-q", "--no-ff", "--no-commit", "side");
    await Deno.mkdir(`${dir}/apps/secret`, { recursive: true });
    await Deno.writeTextFile(`${dir}/apps/secret/Evil.cs`, "e");
    await run(dir, "add", "apps/secret/Evil.cs");
    await run(dir, "-c", "user.name=Jane Acme", "-c", "user.email=jane@example.test", "commit", "-q", "-m", "merge");
    assertEquals((await audit(dir, RULES, "work")).pathHits, ["apps/secret/Evil.cs"]);
  });
});

Deno.test("the company form fails and the person's name passes", async () => {
  await withFixture([
    { files: { "NOTICE.md": "Copyright Jane Acme. Formerly the Acme org." }, message: "docs: notice" },
  ], async (dir) => {
    const report = await audit(dir, RULES, "work");
    assertEquals(report.blobHits.length, 1);
    assertEquals(report.blobHits[0].count, 1);
  });
});

Deno.test("a binary blob is skipped, as filter-repo skips it", async () => {
  await withFixture([
    { files: { "font.woff2": "\u0000\u0001host7\u0000" }, message: "feat: font" },
  ], async (dir) => {
    assertEquals(failures(await audit(dir, RULES, "work")), 0);
  });
});

Deno.test("a commit message that names a removed string fails", async () => {
  await withFixture([
    { files: { "a.txt": "a" }, message: "chore: back up to the hangar" },
  ], async (dir) => {
    const report = await audit(dir, RULES, "work");
    assertEquals(report.messageHits.map((h) => h.rule), ["\\bhangar\\b"]);
  });
});

Deno.test("a second author identity fails", async () => {
  await withFixture([
    { files: { "a.txt": "a" }, message: "one" },
    { files: { "b.txt": "b" }, message: "two", author: "Someone Else <else@example.test>" },
  ], async (dir) => {
    const report = await audit(dir, RULES, "work");
    assertEquals(report.identities.length, 2);
    assertEquals(failures(report), 1);
  });
});

Deno.test("a comment line in a rules file is refused, since filter-repo would replace it", () => {
  assertThrows(() => parseRules("# note\nregex:x==>y"), Error, "comment line");
});

Deno.test("path lists skip comments and blank lines", () => {
  assertEquals(parsePathList("# head\n\napps/x\n  libs/y  \n"), ["apps/x", "libs/y"]);
});

Deno.test("a path matches its own prefix and not a longer sibling", () => {
  assertEquals(isRemovedPath("apps/secret", RULES.paths), true);
  assertEquals(isRemovedPath("apps/secret/a.cs", RULES.paths), true);
  assertEquals(isRemovedPath("apps/secretive/a.cs", RULES.paths), false);
});

Deno.test("--rules-dir is required, and a missing rules dir is an error", async () => {
  assertThrows(() => parseArgs([]), Error, "--rules-dir is required");
  assertEquals(parseArgs(["--rules-dir", "r", "--tree"]).tree, true);
  await assertRejects(() => loadRules("does-not-exist"));
});
