import { assert, assertEquals, assertNotEquals, assertThrows } from "@std/assert";
import {
  admit,
  appendLedger,
  canTransition,
  collapse,
  computeId,
  normaliseExcerpt,
  parseLedger,
  readLedger,
  serialise,
  shapeError,
  transition,
  TransitionError,
  validate,
  type Candidate,
  type Entry,
} from "./ledger.ts";

const NOW = new Date("2026-08-30T03:00:00.000Z");
const RUN = "2026-08-30";

function candidate(over: Partial<Candidate> = {}): Candidate {
  return {
    ruleId: "stale-todo",
    path: "libs/nativeweb.auth/Foo.cs",
    line: 214,
    what: "A TODO from 2025-01 with no owner and no issue.",
    why: "It records an intent nobody is tracking; the code around it has changed twice since.",
    confirm: "git log -S 'TODO: revisit' -- libs/nativeweb.auth/Foo.cs",
    excerpt: "        // TODO: revisit once the token cache lands\n        return null;",
    verified: "verified",
    ...over,
  };
}

function entry(over: Partial<Entry> = {}): Entry {
  return {
    id: "abc123",
    ruleId: "stale-todo",
    path: "libs/nativeweb.auth/Foo.cs",
    line: 214,
    what: "w",
    why: "y",
    confirm: "c",
    verified: "verified",
    state: "new",
    at: NOW.toISOString(),
    reason: "first seen",
    runId: RUN,
    ...over,
  };
}

// --- normalisation and identity (R7) -----------------------------------------------------

Deno.test("normaliseExcerpt strips indentation, blank lines and internal whitespace runs", () => {
  const messy = "\n    if (a   ===  b) {\n\n        return   1;\n    }\n";
  assertEquals(normaliseExcerpt(messy), "if (a === b) {\nreturn 1;\n}");
});

Deno.test("identity ignores the line number, so an edit above a defect does not resurrect it", async () => {
  const before = await computeId(candidate({ line: 214 }));
  const after = await computeId(candidate({ line: 261 }));
  assertEquals(before, after);
});

Deno.test("identity ignores reindentation but not a changed statement", async () => {
  const base = await computeId(candidate());
  const reindented = await computeId(
    candidate({ excerpt: "// TODO: revisit once the token cache lands\n  return null;" }),
  );
  const changed = await computeId(
    candidate({ excerpt: "        // TODO: revisit once the token cache lands\n        return 0;" }),
  );
  assertEquals(base, reindented);
  assertNotEquals(base, changed);
});

Deno.test("identity separates path and rule, and is not confusable across the boundary", async () => {
  const base = await computeId(candidate());
  assertNotEquals(base, await computeId(candidate({ path: "libs/nativeweb.auth/Bar.cs" })));
  assertNotEquals(base, await computeId(candidate({ ruleId: "assertionless-test" })));
  // A field boundary that could be smeared by naive concatenation.
  const a = await computeId({ path: "a/b", ruleId: "c", excerpt: "d" });
  const b = await computeId({ path: "a", ruleId: "b/c", excerpt: "d" });
  assertNotEquals(a, b);
});

Deno.test("a Windows path and a POSIX path are the same finding", async () => {
  assertEquals(
    await computeId(candidate({ path: "libs\\nativeweb.auth\\Foo.cs" })),
    await computeId(candidate({ path: "libs/nativeweb.auth/Foo.cs" })),
  );
});

// --- validation (R6) ---------------------------------------------------------------------

Deno.test("validate accepts a complete candidate", () => {
  assertEquals(validate(candidate()), null);
});

Deno.test("validate names the missing field for every field a reader needs", () => {
  const cases: Array<[Partial<Candidate>, string]> = [
    [{ ruleId: "  " }, "ruleId"],
    [{ path: "" }, "path"],
    [{ what: "" }, "what"],
    [{ why: "   " }, "why"],
    [{ confirm: "" }, "confirm"],
    [{ excerpt: "" }, "excerpt"],
  ];
  for (const [over, field] of cases) {
    const reason = validate(candidate(over));
    assert(reason !== null, `expected ${field} to be rejected`);
    assert(reason.includes(field), `reason ${JSON.stringify(reason)} should name ${field}`);
  }
});

Deno.test("validate rejects a line number that is not a positive integer", () => {
  for (const line of [0, -1, 1.5, Number.NaN]) {
    assert(validate(candidate({ line })) !== null, `expected line ${line} to be rejected`);
  }
  assertEquals(validate(candidate({ line: 1 })), null);
});

// --- state machine (R8) ------------------------------------------------------------------

Deno.test("rejected is absorbing", () => {
  for (const to of ["new", "accepted", "fixed"] as const) {
    assertEquals(canTransition("rejected", to), false);
  }
  const err = assertThrows(
    () => transition(entry({ state: "rejected" }), "accepted", "changed my mind", RUN, NOW),
    TransitionError,
  );
  assert(err.message.includes("absorbing"));
});

Deno.test("a transition without a reason is refused", () => {
  assertThrows(() => transition(entry(), "accepted", "   ", RUN, NOW), TransitionError);
});

Deno.test("a transition the machine does not permit is refused", () => {
  assertThrows(() => transition(entry({ state: "accepted" }), "new", "why", RUN, NOW), TransitionError);
});

Deno.test("a permitted transition stamps the date, the reason and the run", () => {
  const later = new Date("2026-09-02T09:15:00.000Z");
  const next = transition(entry(), "accepted", "real, and worth a branch", "2026-09-02", later);
  assertEquals(next.state, "accepted");
  assertEquals(next.at, later.toISOString());
  assertEquals(next.reason, "real, and worth a branch");
  assertEquals(next.runId, "2026-09-02");
  assertEquals(next.id, entry().id);
});

Deno.test("a fixed finding can regress back to new", () => {
  assertEquals(canTransition("fixed", "new"), true);
  assertEquals(transition(entry({ state: "fixed" }), "new", "regressed", RUN, NOW).state, "new");
});

// --- collapse and parse ------------------------------------------------------------------

Deno.test("collapse takes the last record for an id", () => {
  const current = collapse([
    entry({ id: "x", state: "new" }),
    entry({ id: "y", state: "new" }),
    entry({ id: "x", state: "rejected", reason: "not a defect" }),
  ]);
  assertEquals(current.size, 2);
  assertEquals(current.get("x")?.state, "rejected");
  assertEquals(current.get("y")?.state, "new");
});

Deno.test("one unparseable record costs one record, not the file", () => {
  const text = [JSON.stringify(entry({ id: "a" })), "{ not json", JSON.stringify(entry({ id: "b" }))]
    .join("\n");
  const parsed = parseLedger(text);
  assertEquals(parsed.map((e) => e.id), ["a", "b"]);
});

Deno.test("serialise round-trips through parseLedger", () => {
  const entries = [entry({ id: "a" }), entry({ id: "b", state: "accepted" })];
  assertEquals(parseLedger(serialise(entries)), entries);
});

// --- admit (R6, R7, R8) ------------------------------------------------------------------

Deno.test("an unseen candidate is admitted as new", async () => {
  const result = await admit([candidate()], new Map(), RUN, NOW);
  assertEquals(result.admitted.length, 1);
  assertEquals(result.admitted[0].state, "new");
  assertEquals(result.admitted[0].reason, "first seen");
  assertEquals(result.moved.length, 0);
  assertEquals(result.unchanged.length, 0);
});

Deno.test("the same candidate a second time is reported nowhere", async () => {
  const first = await admit([candidate()], new Map(), RUN, NOW);
  const current = collapse(first.admitted);
  const second = await admit([candidate()], current, "2026-08-31", NOW);
  assertEquals(second.admitted.length, 0);
  assertEquals(second.moved.length, 0);
  assertEquals(second.unchanged.length, 1);
});

Deno.test("a moved finding produces a line-moved record and no new finding", async () => {
  const first = await admit([candidate({ line: 214 })], new Map(), RUN, NOW);
  const current = collapse(first.admitted);
  const second = await admit([candidate({ line: 261 })], current, "2026-08-31", NOW);
  assertEquals(second.admitted.length, 0);
  assertEquals(second.moved.length, 1);
  assertEquals(second.moved[0].line, 261);
  assertEquals(second.moved[0].state, "new");
  assert(second.moved[0].reason.includes("214 -> 261"));
});

Deno.test("a rejected finding is not resurfaced, not even by a line move", async () => {
  const first = await admit([candidate({ line: 214 })], new Map(), RUN, NOW);
  const dismissed = transition(first.admitted[0], "rejected", "intended, documented above", RUN, NOW);
  const current = collapse([first.admitted[0], dismissed]);
  const second = await admit([candidate({ line: 999 })], current, "2026-08-31", NOW);
  assertEquals(second.admitted.length, 0);
  assertEquals(second.moved.length, 0);
  assertEquals(second.unchanged.length, 1);
});

Deno.test("an invalid candidate is dropped with its reason and does not reach the ledger", async () => {
  const result = await admit([candidate({ confirm: "" })], new Map(), RUN, NOW);
  assertEquals(result.admitted.length, 0);
  assertEquals(result.dropped.length, 1);
  assert(result.dropped[0].reason.includes("confirm"));
});

Deno.test("two identical candidates in one run count once", async () => {
  const result = await admit([candidate(), candidate()], new Map(), RUN, NOW);
  assertEquals(result.admitted.length, 1);
  assertEquals(result.dropped.length, 1);
  assert(result.dropped[0].reason.includes("duplicate"));
});

// --- persistence -------------------------------------------------------------------------

Deno.test("appendLedger appends and never truncates", async () => {
  const dir = await Deno.makeTempDir({ prefix: "nw-audit-ledger-" });
  const path = `${dir}/ledger.jsonl`;
  try {
    await appendLedger(path, [entry({ id: "a" })]);
    await appendLedger(path, [entry({ id: "b" })]);
    const entries = await readLedger(path);
    assertEquals(entries.map((e) => e.id), ["a", "b"]);
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("appending nothing does not create a file", async () => {
  const dir = await Deno.makeTempDir({ prefix: "nw-audit-ledger-" });
  const path = `${dir}/ledger.jsonl`;
  try {
    await appendLedger(path, []);
    assertEquals(await readLedger(path), []);
    await assertThrows(() => Deno.statSync(path), Deno.errors.NotFound);
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("a missing ledger reads as empty rather than throwing", async () => {
  assertEquals(await readLedger("/nonexistent/nowhere/ledger.jsonl"), []);
});

// --- shape validation on the way in (pre-merge review) ----------------------------------------

Deno.test("a record that parses but is not an Entry is skipped, not admitted", () => {
  // The failure this prevents: an id and a state but no `what`, which reaches the digest's
  // renderer and throws there — one bad line taking out the whole report.
  const good = JSON.stringify({
    id: "a".repeat(64),
    ruleId: "stale-todo",
    path: "src/A.cs",
    line: 3,
    what: "x",
    why: "y",
    confirm: "z",
    verified: "verified",
    state: "new",
    at: "2026-08-29T00:00:00.000Z",
  });
  const bad = JSON.stringify({ id: "b".repeat(64), state: "new" });
  const entries = parseLedger(`${good}\n${bad}\n`);
  assertEquals(entries.length, 1);
  assertEquals(entries[0].id, "a".repeat(64));
});

Deno.test("shapeError names the first thing wrong", () => {
  assertEquals(shapeError(null), "not an object");
  assertEquals(shapeError([1, 2]), "not an object");
  assertEquals(shapeError("a string"), "not an object");
  assert((shapeError({}) ?? "").includes("'id'"));
});

Deno.test("shapeError rejects a bad line number and a bad state", () => {
  const base = {
    id: "x",
    ruleId: "r",
    path: "p",
    what: "w",
    why: "y",
    confirm: "c",
    at: "t",
    verified: "verified",
    state: "new",
  };
  assert((shapeError({ ...base, line: "3" }) ?? "").includes("line"));
  assert((shapeError({ ...base, line: 3, state: "maybe" }) ?? "").includes("state"));
  assert((shapeError({ ...base, line: 3, verified: "sort of" }) ?? "").includes("verified"));
  assertEquals(shapeError({ ...base, line: 3 }), null);
});

Deno.test("a JSON array on one line does not collapse onto a single undefined key", () => {
  assertEquals(parseLedger("[1,2,3]\n").length, 0);
});
