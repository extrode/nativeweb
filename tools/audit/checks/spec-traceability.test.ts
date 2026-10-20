import { assert, assertEquals } from "@std/assert";
import { type CheckContext, noBlame, noReadFile, type SourceFile } from "./types.ts";
import {
  requirementsIn,
  sectionOf,
  specDoneWithOpenTask,
  specDoneWithUnmetRequirement,
  specRequirementWithoutCriterion,
  statusOf,
  UNMET,
} from "./spec-traceability.ts";
import { validate } from "../ledger.ts";

function ctx(files: SourceFile[], over: Partial<CheckContext> = {}): CheckContext {
  return {
    slice: "docs/specs/999-x",
    files,
    allPaths: new Set<string>(),
    now: new Date("2026-08-30T00:00:00.000Z"),
    blame: noBlame,
    readFile: noReadFile,
    ...over,
  };
}

const SPEC = (status: string, extra = "") =>
  `# 999 — Thing
Status: ${status}
Created: 2026-08-01

## Requirements

- **R1** The first thing.
- **R2** The second thing.

## Acceptance criteria

- **R1** — Given a, when b, then c.
- **R2** — Given d, when e, then f.
${extra}
`;

const specFile = (text: string): SourceFile => ({ path: "docs/specs/999-x/999-spec.md", text });

// --- parsing ---------------------------------------------------------------------------------

Deno.test("sectionOf returns one section and stops at the next heading", () => {
  const reqs = sectionOf(SPEC("draft"), "Requirements");
  assert(reqs.includes("R1"));
  assert(!reqs.includes("Given a"), "should not run into the acceptance section");
});

Deno.test("sectionOf on an absent heading is empty rather than everything", () => {
  assertEquals(sectionOf(SPEC("draft"), "Nonexistent"), "");
});

Deno.test("requirementsIn finds each requirement once, in order", () => {
  assertEquals(requirementsIn(SPEC("draft")).map((r) => r.id), ["R1", "R2"]);
});

Deno.test("requirements are read from the Requirements section only", () => {
  // R9 appears only in the acceptance section; it is not a declared requirement.
  const text = SPEC("draft").replace("- **R2** — Given d", "- **R9** — Given d");
  assertEquals(requirementsIn(text).map((r) => r.id), ["R1", "R2"]);
});

Deno.test("statusOf reads the status line and lowercases it", () => {
  assertEquals(statusOf(SPEC("Done")), "done");
  assertEquals(statusOf(SPEC("draft")), "draft");
  assertEquals(statusOf("# no status here"), null);
});

// --- spec-requirement-without-criterion --------------------------------------------------------

Deno.test("a requirement with no acceptance criterion is reported at its own line", async () => {
  const text = SPEC("draft").replace("- **R2** — Given d, when e, then f.\n", "");
  const found = await specRequirementWithoutCriterion.run(ctx([specFile(text)]));
  assertEquals(found.length, 1);
  assert(found[0].what.startsWith("R2 "));
  assertEquals(text.split(/\r?\n/)[found[0].line - 1], "- **R2** The second thing.");
  assertEquals(validate(found[0]), null);
});

Deno.test("a spec where every requirement has a criterion is clean", async () => {
  assertEquals((await specRequirementWithoutCriterion.run(ctx([specFile(SPEC("draft"))]))).length, 0);
});

Deno.test("a spec with no acceptance section at all says nothing, rather than once per requirement", async () => {
  const text = SPEC("draft").split("## Acceptance criteria")[0];
  assertEquals((await specRequirementWithoutCriterion.run(ctx([specFile(text)]))).length, 0);
});

Deno.test("a file that is not a spec is not examined", async () => {
  const notSpec = { path: "docs/specs/999-x/999-plan.md", text: SPEC("draft") };
  assertEquals((await specRequirementWithoutCriterion.run(ctx([notSpec]))).length, 0);
});

// --- spec-done-with-open-task ------------------------------------------------------------------

const TASKS_OPEN = "- [x] T1 done\n- [ ] T2 not done\n- [x] T3 done\n";
const TASKS_CLOSED = "- [x] T1 done\n- [x] T2 done\n";

Deno.test("a done spec over an unticked task is reported, counting the open tasks", async () => {
  const found = await specDoneWithOpenTask.run(
    ctx([specFile(SPEC("done"))], { readFile: () => Promise.resolve(TASKS_OPEN) }),
  );
  assertEquals(found.length, 1);
  assertEquals(found[0].line, 2, "the finding anchors to the Status line");
  assert(found[0].what.includes("1 unticked task"));
  assert(found[0].what.includes("line 2"));
  assertEquals(validate(found[0]), null);
});

Deno.test("a draft spec over an unticked task is the normal case, not a defect", async () => {
  const found = await specDoneWithOpenTask.run(
    ctx([specFile(SPEC("draft"))], { readFile: () => Promise.resolve(TASKS_OPEN) }),
  );
  assertEquals(found.length, 0);
});

Deno.test("a done spec whose tasks are all ticked is clean", async () => {
  const found = await specDoneWithOpenTask.run(
    ctx([specFile(SPEC("done"))], { readFile: () => Promise.resolve(TASKS_CLOSED) }),
  );
  assertEquals(found.length, 0);
});

Deno.test("a done spec with no task list at all raises nothing", async () => {
  assertEquals((await specDoneWithOpenTask.run(ctx([specFile(SPEC("done"))]))).length, 0);
});

Deno.test("the tasks file is looked for beside the spec, under the matching name", async () => {
  const asked: string[] = [];
  await specDoneWithOpenTask.run(ctx([specFile(SPEC("done"))], {
    readFile: (p) => {
      asked.push(p);
      return Promise.resolve(null);
    },
  }));
  assertEquals(asked, ["docs/specs/999-x/999-tasks.md"]);
});

// --- spec-done-with-unmet-requirement -----------------------------------------------------------

Deno.test("a done spec that records something as unmet is reported", async () => {
  const text = SPEC("done", "\n**R16 UNMET** — the benchmark cannot be re-run.\n");
  const found = await specDoneWithUnmetRequirement.run(ctx([specFile(text)]));
  assertEquals(found.length, 1);
  assertEquals(found[0].line, 2);
  assert(found[0].what.includes("unmet"));
  assertEquals(validate(found[0]), null);
});

Deno.test("the same wording in a draft spec is not a defect", async () => {
  const text = SPEC("draft", "\n**R16 UNMET** — the benchmark cannot be re-run.\n");
  assertEquals((await specDoneWithUnmetRequirement.run(ctx([specFile(text)]))).length, 0);
});

Deno.test("a done spec with no such wording is clean", async () => {
  assertEquals((await specDoneWithUnmetRequirement.run(ctx([specFile(SPEC("done"))]))).length, 0);
});

Deno.test("the three rules carry three distinct ids, so dismissing one does not silence the others", () => {
  const ids = [
    specRequirementWithoutCriterion.ruleId,
    specDoneWithOpenTask.ruleId,
    specDoneWithUnmetRequirement.ruleId,
  ];
  assertEquals(new Set(ids).size, 3);
});

Deno.test("the unmet marker matches the forms this repo's specs actually use", () => {
  // Two of the alternatives were dead: `\b` after a backtick can never match, and without `i`
  // the sentence-initial `Unmet` was missed.
  for (const line of [
    "R16 is UNMET: no harness reproduces the benchmark.",
    "R9 is not met here, deliberately.",
    "Unmet, and recorded as such.",
    "`011-spec.md` is not `done` until T15 runs.",
    "R2 remains outstanding.",
  ]) {
    assert(UNMET.test(line), `not matched: ${line}`);
  }
});

Deno.test("the unmet marker does not fire on ordinary prose", () => {
  for (const line of [
    "Every requirement is met and evidenced.",
    "The status is `done`.",
    "Outstanding work is tracked in work/todo.md.",
  ]) {
    assert(!UNMET.test(line), `wrongly matched: ${line}`);
  }
});
