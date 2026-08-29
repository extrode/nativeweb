import { assert, assertEquals, assertStringIncludes, assertThrows } from "@std/assert";
import {
  applyDecisions,
  checkApproval,
  type Decision,
  decisionsFromArgs,
  type LedgerStore,
  resolveId,
  triage,
  TriageError,
  validateDecision,
} from "./triage.ts";
import { collapse, type Entry, parseLedger, serialise, type State } from "./ledger.ts";

const ID_A = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
const ID_B = "a1b2c3d4ffff0718293a4b5c6d7e8f90";
const ID_C = "9999c3d4e5f60718293a4b5c6d7e8f90";

function entry(id: string, over: Partial<Entry> = {}): Entry {
  return {
    id,
    ruleId: "stale-todo",
    path: "apps/web/Program.cs",
    line: 10,
    what: "A TODO with no owner.",
    why: "Nobody knows whether it still applies.",
    confirm: "git blame -L 10,10 apps/web/Program.cs",
    verified: "verified",
    state: (over.state ?? "new") as State,
    at: over.at ?? "2026-08-20T00:00:00.000Z",
    ...over,
  } as Entry;
}

const NOW = new Date("2026-08-29T09:00:00.000Z");

function memoryStore(initial = ""): LedgerStore & { text: string; appends: number } {
  const store = {
    text: initial,
    appends: 0,
    read: () => Promise.resolve(store.text),
    append: (t: string) => {
      store.appends++;
      store.text += t;
      return Promise.resolve();
    },
  };
  return store;
}

// --- R14: accepting requires a recorded approver ------------------------------------------------

Deno.test("accepting without an approver is refused", () => {
  assert(checkApproval({ id: ID_A, state: "accepted", reason: "real" }) !== null);
});

Deno.test("an approver of only whitespace is not an approver", () => {
  assert(checkApproval({ id: ID_A, state: "accepted", reason: "real", approvedBy: "   " }) !== null);
});

Deno.test("accepting with an approver is allowed", () => {
  assertEquals(checkApproval({ id: ID_A, state: "accepted", reason: "real", approvedBy: "sy" }), null);
});

Deno.test("rejecting needs no approver", () => {
  assertEquals(checkApproval({ id: ID_A, state: "rejected", reason: "cross-repo note" }), null);
});

Deno.test("marking fixed needs no approver", () => {
  assertEquals(checkApproval({ id: ID_A, state: "fixed", reason: "done in 5857bf4" }), null);
});

Deno.test("an unapproved acceptance writes nothing at all", async () => {
  const store = memoryStore(serialise([entry(ID_A)]));
  const before = store.text;
  const result = await triage(store, [{ id: ID_A, state: "accepted", reason: "real" }], "t1", NOW);
  assertEquals(result.applied.length, 0);
  assertEquals(result.refused.length, 1);
  assertEquals(store.text, before);
});

Deno.test("the approver is recorded in the entry's reason, so the ledger says who agreed", async () => {
  const store = memoryStore(serialise([entry(ID_A)]));
  await triage(
    store,
    [{ id: ID_A, state: "accepted", reason: "broken cross-reference", approvedBy: "sy" }],
    "t1",
    NOW,
  );
  const current = collapse(parseLedger(store.text)).get(ID_A)!;
  assertEquals(current.state, "accepted");
  assertStringIncludes(current.reason ?? "", "approved-by: sy");
  assertStringIncludes(current.reason ?? "", "broken cross-reference");
});

// --- R8: reasons, and rejection as an absorbing state -------------------------------------------

Deno.test("a decision with no reason is refused", () => {
  assert(validateDecision({ id: ID_A, state: "rejected", reason: "  " }) !== null);
});

Deno.test("a state that is not a decision is refused", () => {
  const invalid = validateDecision({ id: ID_A, state: "new" as State, reason: "x" });
  assert(invalid !== null);
  assertStringIncludes(invalid, "not a decision");
});

Deno.test("every applied decision carries the date it was taken", () => {
  const result = applyDecisions([entry(ID_A)], [{ id: ID_A, state: "rejected", reason: "no" }], "t1", NOW);
  assertEquals(result.applied[0].at, NOW.toISOString());
});

Deno.test("a rejected finding cannot be revived by a later decision", async () => {
  const store = memoryStore(serialise([entry(ID_A)]));
  await triage(store, [{ id: ID_A, state: "rejected", reason: "not ours" }], "t1", NOW);
  const second = await triage(
    store,
    [{ id: ID_A, state: "accepted", reason: "changed my mind", approvedBy: "sy" }],
    "t2",
    NOW,
  );
  assertEquals(second.applied.length, 0);
  assertStringIncludes(second.refused[0].why, "absorbing");
  assertEquals(collapse(parseLedger(store.text)).get(ID_A)!.state, "rejected");
});

Deno.test("an accepted finding can still be marked fixed", () => {
  const result = applyDecisions(
    [entry(ID_A, { state: "accepted" })],
    [{ id: ID_A, state: "fixed", reason: "landed in 265a77d" }],
    "t1",
    NOW,
  );
  assertEquals(result.applied[0].state, "fixed");
});

Deno.test("accept then fix in one batch works, because later decisions see earlier ones", () => {
  const result = applyDecisions(
    [entry(ID_A)],
    [
      { id: ID_A, state: "accepted", reason: "real", approvedBy: "sy" },
      { id: ID_A, state: "fixed", reason: "already done" },
    ],
    "t1",
    NOW,
  );
  assertEquals(result.applied.map((e) => e.state), ["accepted", "fixed"]);
  assertEquals(result.refused.length, 0);
});

Deno.test("the ledger is appended to, never rewritten", async () => {
  const original = serialise([entry(ID_A), entry(ID_C)]);
  const store = memoryStore(original);
  await triage(store, [{ id: ID_C, state: "rejected", reason: "cross-repo" }], "t1", NOW);
  assert(store.text.startsWith(original));
  assertEquals(parseLedger(store.text).length, 3);
});

// --- id resolution -------------------------------------------------------------------------------

Deno.test("an unambiguous prefix resolves", () => {
  assertEquals(resolveId("9999c3", [ID_A, ID_B, ID_C]), ID_C);
});

Deno.test("an ambiguous prefix is an error, not a guess", () => {
  const thrown = assertThrows(() => resolveId("a1b2c3d4", [ID_A, ID_B]), TriageError);
  assertStringIncludes((thrown as Error).message, "matches 2 findings");
});

Deno.test("a prefix shorter than six characters is refused before it can be ambiguous", () => {
  assertThrows(() => resolveId("a1b", [ID_A]), TriageError, "too short");
});

Deno.test("a prefix matching nothing is an error", () => {
  assertThrows(() => resolveId("ffffff", [ID_A]), TriageError, "no finding matches");
});

Deno.test("id resolution is case-insensitive", () => {
  assertEquals(resolveId("A1B2C3D4E5", [ID_A, ID_B, ID_C]), ID_A);
});

Deno.test("an unresolvable id refuses only itself, and the rest of the batch applies", () => {
  const result = applyDecisions(
    [entry(ID_A), entry(ID_C)],
    [
      { id: "ffffff", state: "rejected", reason: "x" },
      { id: ID_C, state: "rejected", reason: "cross-repo" },
    ],
    "t1",
    NOW,
  );
  assertEquals(result.applied.length, 1);
  assertEquals(result.applied[0].id, ID_C);
  assertEquals(result.refused.length, 1);
});

// --- the audit never triages ----------------------------------------------------------------------

Deno.test("triage on an empty ledger applies nothing and refuses the decision", async () => {
  const store = memoryStore("");
  const result = await triage(store, [{ id: ID_A, state: "rejected", reason: "x" }], "t1", NOW);
  assertEquals(result.applied.length, 0);
  assertEquals(store.text, "");
});

Deno.test("an empty batch touches the store not at all", async () => {
  // Not merely "the content is unchanged": with nothing to say, the write must not happen. Over
  // ssh that write is a connection to the audit host, and creating an empty ledger where none
  // existed would make "no findings" and "never ran" look the same on disk.
  const store = memoryStore(serialise([entry(ID_A)]));
  const before = store.text;
  const result = await triage(store, [], "t1", NOW);
  assertEquals(result.applied.length, 0);
  assertEquals(store.text, before);
  assertEquals(store.appends, 0);
});

Deno.test("a batch where every decision is refused writes nothing to the store", async () => {
  const store = memoryStore(serialise([entry(ID_A)]));
  const result = await triage(store, [{ id: ID_A, state: "accepted", reason: "real" }], "t1", NOW);
  assertEquals(result.refused.length, 1);
  assertEquals(store.appends, 0);
});

Deno.test("the decision carries the run id, so a triage run is traceable", () => {
  const result = applyDecisions([entry(ID_A)], [{ id: ID_A, state: "rejected", reason: "x" }], "t-42", NOW);
  assertEquals((result.applied[0] as Entry & { runId?: string }).runId, "t-42");
});

// --- CLI argument shaping -------------------------------------------------------------------------

Deno.test("no --id yields no decisions, so a bare invocation cannot change anything", () => {
  assertEquals(decisionsFromArgs({}), []);
});

Deno.test("a missing --state becomes an invalid decision rather than a default one", () => {
  const [decision] = decisionsFromArgs({ id: ID_A, reason: "x" });
  assert(validateDecision(decision) !== null);
});

Deno.test("--approved-by is carried onto the decision", () => {
  const [decision] = decisionsFromArgs({
    id: ID_A,
    state: "accepted",
    reason: "x",
    "approved-by": "sy",
  }) as Decision[];
  assertEquals(decision.approvedBy, "sy");
});
