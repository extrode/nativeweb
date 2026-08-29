import { assert, assertEquals, assertRejects, assertStringIncludes } from "@std/assert";
import {
  buildDigest,
  costTrend,
  datesBetween,
  type Digest,
  DigestUnreachable,
  health,
  loadDigest,
  rank,
  render,
  shellQuote,
  type Source,
} from "./digest.ts";
import type { Entry, State, Verification } from "./ledger.ts";
import type { SliceRow } from "./slices.ts";
import type { RunRecord } from "./run.ts";

function entry(over: Partial<Entry> = {}): Entry {
  return {
    id: over.id ?? "aaaa1111",
    ruleId: over.ruleId ?? "stale-todo",
    path: over.path ?? "apps/web/Program.cs",
    line: over.line ?? 10,
    what: over.what ?? "A TODO with no owner.",
    why: over.why ?? "Nobody knows whether it still applies.",
    confirm: over.confirm ?? "git blame -L 10,10 apps/web/Program.cs",
    verified: (over.verified ?? "verified") as Verification,
    state: (over.state ?? "new") as State,
    at: over.at ?? "2026-08-20T00:00:00.000Z",
    reason: over.reason,
  } as Entry;
}

function record(over: Partial<RunRecord> = {}): RunRecord {
  return {
    runId: over.runId ?? "r1",
    startedAt: over.startedAt ?? "2026-08-20T02:00:00.000Z",
    endedAt: over.endedAt ?? "2026-08-20T02:01:00.000Z",
    slice: over.slice ?? "apps/web",
    outcome: over.outcome ?? "ok",
    error: over.error ?? null,
    candidates: over.candidates ?? 0,
    refuted: over.refuted ?? 0,
    unevaluated: over.unevaluated ?? 0,
    discarded: over.discarded ?? 0,
    malformed: over.malformed ?? 0,
    corpusLeaks: over.corpusLeaks ?? 0,
    admitted: over.admitted ?? 0,
    moved: over.moved ?? 0,
    redactions: over.redactions ?? 0,
    tokensIn: over.tokensIn ?? 0,
    tokensOut: over.tokensOut ?? 0,
    costUsd: over.costUsd ?? null,
    durationMs: over.durationMs ?? 60_000,
    modelUsed: over.modelUsed ?? null,
  } as RunRecord;
}

// --- R9: the same list twice, and only what is untriaged -------------------------------------

Deno.test("running the digest twice with no triage between produces the same list", () => {
  const ledger = [entry({ id: "a" }), entry({ id: "b", path: "apps/web/Home.cs" })];
  const now = new Date("2026-08-29T00:00:00.000Z");
  const first = buildDigest(ledger, [], [record()], now);
  const second = buildDigest(ledger, [], [record()], now);
  assertEquals(first.untriaged.map((e) => e.id), second.untriaged.map((e) => e.id));
  assertEquals(first.untriaged.length, 2);
});

Deno.test("a triaged finding drops out of the digest, and the rest stay", () => {
  const ledger = [
    entry({ id: "a" }),
    entry({ id: "b", path: "apps/web/Home.cs" }),
    // Same id as `a`, later: the collapse rule means this is `a`'s current state.
    entry({ id: "a", state: "rejected", at: "2026-08-21T00:00:00.000Z" }),
  ];
  const digest = buildDigest(ledger, [], [record()], new Date("2026-08-29T00:00:00.000Z"));
  assertEquals(digest.untriaged.map((e) => e.id), ["b"]);
});

Deno.test("a rejected finding never comes back, however many times it is re-found", () => {
  // R8's absorbing state, read from the digest's side: the whole point of `rejected` is that
  // dismissing a finding costs one decision, not one every morning.
  const ledger = [
    entry({ id: "a" }),
    entry({ id: "a", state: "rejected", at: "2026-08-21T00:00:00.000Z" }),
    entry({ id: "a", at: "2026-08-22T00:00:00.000Z" }),
    entry({ id: "a", at: "2026-08-23T00:00:00.000Z" }),
  ];
  const digest = buildDigest(ledger, [], [record()], new Date("2026-08-29T00:00:00.000Z"));
  assertEquals(digest.untriaged.length, 0);
});

Deno.test("verified findings are ranked above unverified ones", () => {
  const ordered = rank([
    entry({ id: "u", verified: "unverified" }),
    entry({ id: "v", verified: "verified" }),
  ]);
  assertEquals(ordered.map((e) => e.id), ["v", "u"]);
});

Deno.test("within a verification tier the oldest finding is listed first", () => {
  const ordered = rank([
    entry({ id: "new", at: "2026-08-25T00:00:00.000Z" }),
    entry({ id: "old", at: "2026-08-01T00:00:00.000Z" }),
  ]);
  assertEquals(ordered.map((e) => e.id), ["old", "new"]);
});

Deno.test("ranking does not mutate the list it is given", () => {
  const input = [entry({ id: "u", verified: "unverified" }), entry({ id: "v" })];
  rank(input);
  assertEquals(input.map((e) => e.id), ["u", "v"]);
});

// --- R12: absence reported as absence ---------------------------------------------------------

Deno.test("a day with no run at all is reported as a gap", () => {
  const h = health([
    record({ runId: "r1", startedAt: "2026-08-20T02:00:00.000Z" }),
    record({ runId: "r2", startedAt: "2026-08-23T02:00:00.000Z" }),
  ]);
  assertEquals(h.gaps, ["2026-08-21", "2026-08-22"]);
});

Deno.test("consecutive daily runs leave no gap", () => {
  const h = health([
    record({ runId: "r1", startedAt: "2026-08-20T02:00:00.000Z" }),
    record({ runId: "r2", startedAt: "2026-08-21T02:00:00.000Z" }),
  ]);
  assertEquals(h.gaps, []);
});

Deno.test("a single run cannot imply a gap", () => {
  assertEquals(health([record()]).gaps, []);
});

Deno.test("silence since the last run is a gap, measured against today", () => {
  // The direction that matters. A scheduler that died produces no run *after* the last one, so
  // there is nothing between first and last to be missing: measuring only that window renders a
  // month of silence as a healthy history.
  const h = health(
    [record({ startedAt: "2026-08-20T02:00:00.000Z" })],
    new Date("2026-08-24T09:00:00.000Z"),
  );
  assertEquals(h.gaps, ["2026-08-21", "2026-08-22", "2026-08-23", "2026-08-24"]);
});

Deno.test("a run earlier today is not a gap", () => {
  const h = health(
    [record({ startedAt: "2026-08-24T02:00:00.000Z" })],
    new Date("2026-08-24T23:00:00.000Z"),
  );
  assertEquals(h.gaps, []);
});

Deno.test("a dead scheduler is rendered as needing attention, not as a clean repository", () => {
  const digest = buildDigest(
    [],
    [],
    [record({ startedAt: "2026-07-20T02:00:00.000Z" })],
    new Date("2026-08-24T09:00:00.000Z"),
  );
  assert(digest.health.gaps.length > 30);
  const text = render(digest);
  assertStringIncludes(text, "needs attention");
  assertStringIncludes(text, "Absence of findings on those days means absence of looking");
});

Deno.test("a history whose only anomaly is a bounded run still renders a warning", () => {
  // `now` is the run's own day on purpose: with any later date the staleness gap would enter the
  // warning block by itself and the assertion would pass without the bounded case being read.
  const digest = buildDigest(
    [],
    [],
    [record({ startedAt: "2026-08-20T02:00:00.000Z", outcome: "bounded" })],
    new Date("2026-08-20T09:00:00.000Z"),
  );
  assertEquals(digest.health.gaps, [], "the fixture must have no gap, or this proves nothing");
  assertEquals(digest.health.failed.length, 0);
  const text = render(digest);
  assertStringIncludes(text, "needs attention");
  assertStringIncludes(text, "not fully read");
});

Deno.test("gap detection crosses a month boundary", () => {
  assertEquals(datesBetween("2026-08-30", "2026-09-02"), [
    "2026-08-30",
    "2026-08-31",
    "2026-09-01",
    "2026-09-02",
  ]);
});

Deno.test("a failed run is carried into health with its error", () => {
  const h = health([record({ outcome: "failed", error: "clone refused" })]);
  assertEquals(h.failed.length, 1);
  assertEquals(h.failed[0].error, "clone refused");
});

Deno.test("a bounded run is reported separately from a failure", () => {
  const h = health([record({ outcome: "bounded" })]);
  assertEquals(h.failed.length, 0);
  assertEquals(h.bounded.length, 1);
});

Deno.test("corpus leaks across runs are summed, because one is already too many", () => {
  assertEquals(health([record({ corpusLeaks: 1 }), record({ corpusLeaks: 2 })]).corpusLeaks, 3);
});

Deno.test("no runs at all is a distinct state from no findings", () => {
  const digest = buildDigest([], [], [], new Date("2026-08-29T00:00:00.000Z"));
  assertEquals(digest.health.runs, 0);
  assertEquals(digest.health.lastRunAt, null);
  const text = render(digest);
  assertStringIncludes(text, "No run has ever completed");
  assertStringIncludes(text, "not because the repository is clean");
});

Deno.test("no findings from one audited slice is not a statement about the repository", () => {
  const coverage: SliceRow[] = [
    { slice: "docs", lastAuditedAt: "2026-08-29T00:00:00.000Z", findingsAtLastAudit: 0 },
    { slice: "apps/web", lastAuditedAt: null, findingsAtLastAudit: 0 },
  ];
  const text = render(buildDigest([], coverage, [record()], new Date()));
  assertStringIncludes(text, "None raised");
  assertStringIncludes(text, "not a statement about the repository");
  assert(!text.includes("has been triaged"));
});

Deno.test("all-decided is worded differently from none-raised", () => {
  const ledger = [entry({ id: "a" }), entry({ id: "a", state: "rejected", at: "2026-08-21T00:00:00.000Z" })];
  const text = render(buildDigest(ledger, [], [record()], new Date()));
  assertStringIncludes(text, "None outstanding");
  assertStringIncludes(text, "All 1 findings so far have been decided");
});

Deno.test("the rendered report never calls an empty ledger a clean repository", () => {
  const text = render(buildDigest([], [], [], new Date("2026-08-29T00:00:00.000Z")));
  assert(!/no (defects|problems) found/i.test(text));
  assert(!/repository is clean\b(?!,)/i.test(text.replace(/not because[^\n]*/g, "")));
});

// --- R11: cost as a trend ---------------------------------------------------------------------

Deno.test("the cost trend totals tokens across runs", () => {
  const trend = costTrend([
    record({ runId: "r1", tokensIn: 100, tokensOut: 20 }),
    record({ runId: "r2", tokensIn: 300, tokensOut: 40 }),
  ]);
  assertEquals(trend.tokensIn, 400);
  assertEquals(trend.tokensOut, 60);
  assertEquals(trend.runs, 2);
});

Deno.test("with no price configured the trend claims no dollar figure", () => {
  // A fabricated cost is worse than no cost: it would be believed.
  const trend = costTrend([record({ tokensIn: 1000, tokensOut: 500 })]);
  assertEquals(trend.costUsd, null);
  assertStringIncludes(
    render(buildDigest([], [], [record({ tokensIn: 1000 })], new Date())),
    "No price configured",
  );
});

Deno.test("priced runs are summed and unpriced ones do not zero the total", () => {
  const trend = costTrend([
    record({ runId: "r1", costUsd: 0.01 }),
    record({ runId: "r2", costUsd: null }),
    record({ runId: "r3", costUsd: 0.02 }),
  ]);
  assertEquals(trend.costUsd !== null && Math.abs(trend.costUsd - 0.03) < 1e-9, true);
});

Deno.test("the recent trend is most-recent-first and bounded", () => {
  const records = Array.from({ length: 10 }, (_, i) =>
    record({ runId: `r${i}`, startedAt: `2026-08-${String(10 + i).padStart(2, "0")}T02:00:00.000Z` }));
  const trend = costTrend(records, 3);
  assertEquals(trend.recent.map((r) => r.runId), ["r9", "r8", "r7"]);
});

// --- rendering --------------------------------------------------------------------------------

Deno.test("the report names the file, line, what, why and how to confirm", () => {
  const text = render(buildDigest([entry()], [], [record()], new Date()));
  assertStringIncludes(text, "apps/web/Program.cs:10");
  assertStringIncludes(text, "A TODO with no owner.");
  assertStringIncludes(text, "Nobody knows whether it still applies.");
  assertStringIncludes(text, "git blame -L 10,10");
});

Deno.test("a pipe in a finding does not break the summary table", () => {
  const text = render(buildDigest([entry({ what: "a | b" })], [], [record()], new Date()));
  const row = text.split("\n").find((l) => l.includes("apps/web/Program.cs:10") && l.startsWith("|"));
  assert(row !== undefined);
  assertEquals(row.split(/(?<!\\)\|/).length - 1, 6); // 5 cells => 6 separators
});

Deno.test("health is rendered above the findings, so a dead scheduler is read first", () => {
  const digest = buildDigest(
    [entry()],
    [],
    [
      record({ runId: "r1", startedAt: "2026-08-20T02:00:00.000Z", outcome: "failed", error: "boom" }),
      record({ runId: "r2", startedAt: "2026-08-23T02:00:00.000Z" }),
    ],
    new Date(),
  );
  const text = render(digest);
  assert(text.indexOf("needs attention") < text.indexOf("Untriaged findings"));
  assertStringIncludes(text, "boom");
  assertStringIncludes(text, "2026-08-21");
});

Deno.test("a corpus leak is rendered as a defect in the audit, not in the repository", () => {
  const text = render(buildDigest([], [], [record({ corpusLeaks: 2 })], new Date()));
  assertStringIncludes(text, "R13 violation");
  assertStringIncludes(text, "defect in the audit, not in the repository");
});

Deno.test("coverage is rendered with the stalest slice named", () => {
  const coverage: SliceRow[] = [
    { slice: "apps/web", lastAuditedAt: "2026-08-01T00:00:00.000Z", findingsAtLastAudit: 1 },
    { slice: "libs/x", lastAuditedAt: null, findingsAtLastAudit: 0 },
  ];
  const text = render(buildDigest([], coverage, [record()], new Date()));
  assertStringIncludes(text, "1 of 2 slices audited, 1 never");
  assertStringIncludes(text, "apps/web");
});

Deno.test("the report states that nothing has been changed without the user's say-so", () => {
  const text = render(buildDigest([entry()], [], [record()], new Date()));
  assertStringIncludes(text, "nothing will without your say-so");
});

// --- loading ----------------------------------------------------------------------------------

function fakeSource(files: Record<string, string>, runs: string[] = []): Source {
  return {
    readText: (p) => Promise.resolve(files[p] ?? null),
    listRuns: () => Promise.resolve(runs),
  };
}

Deno.test("loadDigest reads ledger, coverage and every run record", async () => {
  const source = fakeSource({
    "/s/ledger/nativeweb.jsonl": JSON.stringify(entry()) + "\n",
    "/s/ledger/nativeweb-coverage.json": JSON.stringify([
      { slice: "apps/web", lastAuditedAt: "2026-08-01T00:00:00.000Z", findingsAtLastAudit: 0 },
    ]),
    "/s/runs/r1.json": JSON.stringify(record({ tokensIn: 7 })),
    "/s/runs/r2.json": JSON.stringify(record({ runId: "r2", tokensIn: 5 })),
  }, ["/s/runs/r1.json", "/s/runs/r2.json"]);

  const digest = await loadDigest(source, "/s", "nativeweb", new Date());
  assertEquals(digest.untriaged.length, 1);
  assertEquals(digest.coverage.total, 1);
  assertEquals(digest.cost.tokensIn, 12);
});

Deno.test("a store that does not exist yet reads as no runs, not as a crash", async () => {
  const digest = await loadDigest(fakeSource({}), "/s", "nativeweb", new Date());
  assertEquals(digest.health.runs, 0);
  assertEquals(digest.untriaged.length, 0);
  assertStringIncludes(render(digest), "No run has ever completed");
});

Deno.test("one unreadable run record does not discard the readable ones", async () => {
  const source = fakeSource({
    "/s/runs/good.json": JSON.stringify(record({ tokensIn: 9 })),
    "/s/runs/bad.json": "{not json",
  }, ["/s/runs/bad.json", "/s/runs/good.json"]);
  const digest = await loadDigest(source, "/s", "nativeweb", new Date());
  assertEquals(digest.health.runs, 1);
  assertEquals(digest.cost.tokensIn, 9);
});

Deno.test("a corrupt coverage file leaves coverage empty rather than throwing", async () => {
  const source = fakeSource({ "/s/ledger/nativeweb-coverage.json": "[[[" });
  const digest = await loadDigest(source, "/s", "nativeweb", new Date());
  assertEquals(digest.coverage.total, 0);
});

Deno.test("an unreachable store refuses to produce a digest rather than an empty one", async () => {
  // The failure this prevents: ssh fails, every read returns null, and the report says
  // "No run has ever completed. There are no findings because nothing has looked" — a claim
  // about the audit's history made on the strength of a network error.
  const source: Source = {
    ...fakeSource({}),
    unreachable: () => Promise.resolve("ssh audit-host.local failed (255): Host is down"),
  };
  const thrown = await assertRejects(
    () => loadDigest(source, "/s", "nativeweb", new Date()),
    DigestUnreachable,
  );
  assertStringIncludes(thrown.message, "Host is down");
  assertStringIncludes(thrown.message, "nothing was looked at");
});

Deno.test("a reachable store produces a digest as usual", async () => {
  const source: Source = { ...fakeSource({}), unreachable: () => Promise.resolve(null) };
  const digest = await loadDigest(source, "/s", "nativeweb", new Date());
  assertEquals(digest.health.runs, 0);
});

Deno.test("a remote path is quoted, so a space reads the intended file", () => {
  assertEquals(shellQuote("/home/user/audit store/ledger.jsonl"), "'/home/user/audit store/ledger.jsonl'");
  assertEquals(shellQuote("a'b"), `'a'\\''b'`);
  assertEquals(shellQuote("x; rm -rf ~"), "'x; rm -rf ~'");
});

Deno.test("the digest is JSON-serialisable, so a report can be archived as data", () => {
  const digest: Digest = buildDigest([entry()], [], [record()], new Date());
  const round = JSON.parse(JSON.stringify(digest)) as Digest;
  assertEquals(round.untriaged[0].id, "aaaa1111");
});
