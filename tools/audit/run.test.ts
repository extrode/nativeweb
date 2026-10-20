import { assert, assertEquals } from "@std/assert";
import {
  auditCorpusLeaks,
  CHECKS,
  makeBlame,
  runOnce,
  shouldStampCoverage,
  storePaths,
  withThrowawayClone,
} from "./run.ts";
import { readLedger } from "./ledger.ts";
import { readCoverage } from "./slices.ts";

async function sh(cwd: string, args: string[]): Promise<void> {
  const { code, stderr } = await new Deno.Command("git", { args, cwd, stdout: "null", stderr: "piped" })
    .output();
  if (code !== 0) throw new Error(`git ${args.join(" ")}: ${new TextDecoder().decode(stderr)}`);
}

/** A real git repository with a planted defect in each of two directories. */
async function fixture(): Promise<{ repo: string; store: string; dispose: () => Promise<void> }> {
  const root = await Deno.makeTempDir({ prefix: "nw-audit-fixture-" });
  const repo = `${root}/repo`;
  const store = `${root}/store`;
  await Deno.mkdir(`${repo}/src`, { recursive: true });
  await Deno.mkdir(`${repo}/tests`, { recursive: true });
  await Deno.mkdir(store, { recursive: true });

  await Deno.writeTextFile(`${repo}/src/Service.cs`, "class Service {\n  void Run() { }\n}\n");
  await Deno.writeTextFile(
    `${repo}/tests/ServiceTests.cs`,
    "public class T {\n  [Fact]\n  public void Runs() {\n    new Service().Run();\n  }\n}\n",
  );
  await Deno.writeTextFile(
    `${repo}/README.md`,
    "See `src/Gone.cs` for detail.\n" +
      "Fetch `docs/Missing.md` with Bearer abcdefghijklmnopqrstuvwxyz012345 as the token.\n",
  );

  await sh(repo, ["init", "--quiet", "-b", "main"]);
  await sh(repo, ["config", "user.email", "audit@example.invalid"]);
  await sh(repo, ["config", "user.name", "Audit Fixture"]);
  await sh(repo, ["add", "-A"]);
  await sh(repo, ["commit", "--quiet", "-m", "fixture"]);

  return { repo, store, dispose: () => Deno.remove(root, { recursive: true }) };
}

const BASE = {
  now: new Date("2026-08-30T03:00:00.000Z"),
  useModel: false,
  sliceByteBound: 120_000,
};

Deno.test({
  name: "the throwaway clone is removed when the body succeeds",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      let seen = "";
      await withThrowawayClone(f.repo, (dir) => {
        seen = dir;
        return Promise.resolve();
      });
      assert(seen.length > 0);
      assertEquals(await Deno.stat(seen).then(() => true).catch(() => false), false);
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "the throwaway clone is removed when the body throws, and the error survives",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      let seen = "";
      let caught: Error | null = null;
      try {
        await withThrowawayClone(f.repo, (dir) => {
          seen = dir;
          throw new Error("boom");
        });
      } catch (cause) {
        caught = cause as Error;
      }
      assertEquals(caught?.message, "boom");
      assertEquals(await Deno.stat(seen).then(() => true).catch(() => false), false);
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "a run leaves the audited repository untouched (R1)",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const before = await new Deno.Command("git", {
        args: ["-C", f.repo, "status", "--porcelain"],
        stdout: "piped",
      }).output();
      await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r1" });
      const after = await new Deno.Command("git", {
        args: ["-C", f.repo, "status", "--porcelain"],
        stdout: "piped",
      }).output();
      assertEquals(new TextDecoder().decode(after.stdout), new TextDecoder().decode(before.stdout));
      assertEquals(new TextDecoder().decode(after.stdout).trim(), "", "the fixture must stay clean");
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "a mechanical run finds the planted defects and writes ledger, coverage and a record",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const record = await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r1" });
      assertEquals(record.outcome, "ok");
      assertEquals(record.modelUsed, false);
      assertEquals(record.tokensIn, 0);
      assert(record.durationMs >= 0);

      const paths = storePaths(f.store);
      const coverage = await readCoverage(paths.coverage);
      assertEquals(coverage.map((r) => r.slice).sort(), [".", "src", "tests"]);
      // R5: the first run took the first never-audited slice, and stamped only that one.
      assertEquals(coverage.filter((r) => r.lastAuditedAt !== null).length, 1);
      assertEquals(record.slice, ".");

      const ledger = await readLedger(paths.ledger);
      assertEquals(ledger.length, record.admitted);
      const record2 = JSON.parse(await Deno.readTextFile(`${paths.runs}/r1.json`));
      assertEquals(record2.runId, "r1");
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "the README slice yields the missing-file finding, with a confirmable command",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r1", forceSlice: "." });
      const ledger = await readLedger(storePaths(f.store).ledger);
      const missing = ledger.filter((e) => e.ruleId === "doc-names-missing-file");
      assertEquals(missing.map((e) => e.line), [1, 2], "one finding per missing path");
      assertEquals(new Set(missing.map((e) => e.path)), new Set(["README.md"]));
      assert(missing[0].confirm.includes("src/Gone.cs"));
      assert(missing[1].confirm.includes("docs/Missing.md"));
      assertEquals(missing[0].state, "new");
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "the tests slice yields the assertionless-test finding",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r1", forceSlice: "tests" });
      const ledger = await readLedger(storePaths(f.store).ledger);
      assertEquals(ledger.map((e) => e.ruleId), ["assertionless-test"]);
      assert(ledger[0].what.includes("Runs"));
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "a second run over the same slice admits nothing new (R7)",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const first = await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r1", forceSlice: "." });
      const second = await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r2", forceSlice: "." });
      assert(first.admitted > 0, "the first run should find something");
      assertEquals(second.admitted, 0);
      assertEquals(second.moved, 0);
      assertEquals((await readLedger(storePaths(f.store).ledger)).length, first.admitted);
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "successive runs walk to a different slice each time (R5)",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const slices: Array<string | null> = [];
      for (let i = 1; i <= 3; i++) {
        const now = new Date(BASE.now.getTime() + i * 86_400_000);
        slices.push((await runOnce({ ...BASE, now, source: f.repo, store: f.store, runId: `r${i}` })).slice);
      }
      assertEquals(new Set(slices).size, 3, `expected three distinct slices, got ${slices}`);
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "a run against a source that cannot be cloned still writes a failed record (R12)",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const store = await Deno.makeTempDir({ prefix: "nw-audit-store-" });
    try {
      const record = await runOnce({
        ...BASE,
        source: `${store}/there-is-no-repo-here`,
        store,
        runId: "bad",
      });
      assertEquals(record.outcome, "failed");
      assert((record.error ?? "").length > 0, "the error must be recorded, not swallowed");
      const written = JSON.parse(await Deno.readTextFile(`${store}/runs/bad.json`));
      assertEquals(written.outcome, "failed");
      assertEquals(written.slice, null);
    } finally {
      await Deno.remove(store, { recursive: true });
    }
  },
});

Deno.test({
  name: "asking for the model with no key available fails loudly rather than silently skipping",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const record = await runOnce({
        ...BASE,
        useModel: true,
        apiKey: null,
        source: f.repo,
        store: f.store,
        runId: "nokey",
        forceSlice: ".",
      });
      // An explicit null means "there is no key". The run must fail saying so, and must not
      // fall back to a key on disk: `options.apiKey ?? await loadApiKey()` did exactly that,
      // and this test made a real billed API call until it was fixed.
      assertEquals(record.outcome, "failed");
      assertEquals(record.modelUsed, false);
      assert((record.error ?? "").includes("ZAI_API_KEY"), record.error);
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "cost is null unless a price is supplied, rather than a made-up number",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const bare = await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "c1" });
      assertEquals(bare.costUsd, null);
      const priced = await runOnce({
        ...BASE,
        source: f.repo,
        store: f.store,
        runId: "c2",
        costPerMTokIn: 1,
        costPerMTokOut: 2,
      });
      assertEquals(priced.costUsd, 0, "no tokens spent means no cost, but a number rather than null");
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "blame answers for a committed line and declines rather than throwing for a missing file",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const blame = makeBlame(f.repo);
      const hit = await blame("src/Service.cs", 1);
      assert(hit !== null, "a committed line should have blame");
      assertEquals(hit.authorName, "Audit Fixture");
      assertEquals(await blame("src/NotHere.cs", 1), null);
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "a secret in a finding's excerpt is redacted before it reaches the ledger (R13)",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const record = await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r1", forceSlice: "." });
      assert(record.redactions > 0, "the planted token should have been redacted");
      // R13's own assertion, run over everything written: the audit checks itself.
      assertEquals(record.corpusLeaks, 0);
      assertEquals(await auditCorpusLeaks(storePaths(f.store)), 0);

      // And directly: the value appears in nothing the audit wrote. The ledger stores no
      // excerpt at all, which is why the marker is absent too — redaction still matters,
      // because identity (R7) is hashed over the redacted excerpt.
      const paths = storePaths(f.store);
      for (const p of [paths.ledger, paths.coverage, `${paths.runs}/r1.json`]) {
        const raw = await Deno.readTextFile(p);
        assert(!raw.includes("abcdefghijklmnopqrstuvwxyz012345"), `the token reached ${p}`);
      }

      // The finding itself must survive: redaction removes the value, not the defect.
      const ledger = await readLedger(paths.ledger);
      assert(
        ledger.some((e) => e.ruleId === "doc-names-missing-file" && e.confirm.includes("docs/Missing.md")),
        "the missing-file finding should still be there",
      );
    } finally {
      await f.dispose();
    }
  },
});

Deno.test("every registered rule has a distinct id and a description", () => {
  const ids = CHECKS.map((c) => c.ruleId);
  assertEquals(new Set(ids).size, ids.length, `duplicate rule id in ${ids}`);
  for (const check of CHECKS) assert(check.describe.trim().length > 0, `${check.ruleId} has no describe`);
});

// --- what the pre-merge review found -----------------------------------------------------------

Deno.test({
  name: "a --slice that names nothing is a failed run, not a healthy empty one",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      const record = await runOnce({
        ...BASE,
        source: f.repo,
        store: f.store,
        runId: "r-typo",
        forceSlice: "src-typo",
      });
      // `ok` here would be counted by the digest as a day covered, with zero findings: absence
      // of looking presented as absence of findings.
      assertEquals(record.outcome, "failed");
      assertEquals(record.slice, null);
      assert((record.error ?? "").includes("src-typo"));
    } finally {
      await f.dispose();
    }
  },
});

Deno.test({
  name: "a mechanical-only run stamps the slice, so the rotation advances",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    const f = await fixture();
    try {
      await runOnce({ ...BASE, source: f.repo, store: f.store, runId: "r1", forceSlice: "src" });
      const coverage = await readCoverage(storePaths(f.store).coverage);
      const row = coverage.find((r) => r.slice === "src");
      assert(row !== undefined && row.lastAuditedAt !== null);
    } finally {
      await f.dispose();
    }
  },
});

Deno.test("a run that knows its answer is partial does not claim the slice was read", () => {
  // Stamping sends the slice to the back of the staleness queue. Over-bound (the reading pass
  // skipped it entirely) and truncated (the model stopped at max_tokens) are both cases where
  // the run knows it did not read the slice through.
  assertEquals(shouldStampCoverage(false, false), true, "a complete run stamps");
  assertEquals(shouldStampCoverage(true, false), false, "an over-bound slice does not");
  assertEquals(shouldStampCoverage(false, true), false, "a truncated answer does not");
  assertEquals(shouldStampCoverage(true, true), false);
});

Deno.test({
  name: "the error on a failed run is redacted before it reaches the run record",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    // A ModelError's message carries the upstream response body verbatim. An upstream that
    // echoes the request has just put the API key into a file the corpus scan only sees after
    // it has been written.
    const root = await Deno.makeTempDir({ prefix: "nw-audit-err-" });
    try {
      const store = `${root}/store`;
      await Deno.mkdir(store, { recursive: true });
      const record = await runOnce({
        ...BASE,
        source: `${root}/no-such-repo-Bearer abcdefghijklmnopqrstuvwxyz012345`,
        store,
        runId: "r-fail",
      });
      assertEquals(record.outcome, "failed");
      assert(!(record.error ?? "").includes("abcdefghijklmnopqrstuvwxyz012345"));
      assert((record.error ?? "").includes("[redacted:"));
    } finally {
      await Deno.remove(root, { recursive: true });
    }
  },
});

Deno.test({
  name: "the clone is not shallow, so blame can see history older than the run",
  permissions: { read: true, write: true, run: ["git"], env: true, net: false },
  async fn() {
    // stale-todo decides staleness from blame author-time. A shallow clone attributes every
    // pre-boundary line to the boundary commit with a recent time, so the rule would silently
    // never fire on the audit host.
    const f = await fixture();
    try {
      await withThrowawayClone(f.repo, async (dir) => {
        const shallow = await Deno.stat(`${dir}/.git/shallow`).then(() => true).catch(() => false);
        assertEquals(shallow, false);
        const blame = await makeBlame(dir)("src/Service.cs", 1);
        assert(blame !== null);
      });
    } finally {
      await f.dispose();
    }
  },
});
