// Three ways a spec and what it claims can come apart.
//
// Three rule ids rather than one, because they are three different arguments and the user may
// well agree with one and dismiss another. `rejected` is absorbing (R8), so folding them into a
// single rule would mean dismissing one silences the other two forever.
//
//   spec-requirement-without-criterion   a requirement nothing says how to check
//   spec-done-with-open-task             `Status: done` over an unticked task
//   spec-done-with-unmet-requirement     `Status: done` over a requirement the spec calls unmet
//
// This repository's own history is the reason the last two are worth checking rather than
// assumed: 008, 011 and 012 each stayed `draft` because a requirement was genuinely unmet, and
// each time it took a person re-reading the spec to notice. The failure these rules catch is the
// same situation with the status flipped, which nobody would notice at all.

import type { Candidate } from "../ledger.ts";
import { type Check, type CheckContext, excerptAt, lineAt } from "./types.ts";

const SPEC_FILE = /(?:^|\/)(\d{3})-spec\.md$/;
const STATUS = /^Status:\s*(\w+)/m;

/** `- **R12** text` or `- R12 text`, as written in this repo's specs. */
const REQUIREMENT = /^[-*]\s+\*{0,2}(R\d+)\*{0,2}\s/gm;

/** A requirement referenced from prose: `**R12**`, `R12 —`, `| R12 |`. */
function mentions(text: string, id: string): boolean {
  return new RegExp(`\\b${id}\\b`).test(text);
}

/** The `## Acceptance criteria` section, or empty when the spec has none. */
export function sectionOf(text: string, heading: string): string {
  const re = new RegExp(`^##\\s+${heading}\\s*$`, "im");
  const start = re.exec(text);
  if (start === null) return "";
  const from = start.index + start[0].length;
  const next = /^##\s+/m.exec(text.slice(from));
  return next === null ? text.slice(from) : text.slice(from, from + next.index);
}

export function requirementsIn(text: string): Array<{ id: string; index: number }> {
  const section = sectionOf(text, "Requirements");
  if (section.length === 0) return [];
  const offset = text.indexOf(section);
  const out: Array<{ id: string; index: number }> = [];
  const re = new RegExp(REQUIREMENT.source, "gm");
  let m: RegExpExecArray | null;
  while ((m = re.exec(section)) !== null) out.push({ id: m[1], index: offset + m.index });
  return out;
}

export function statusOf(text: string): string | null {
  return STATUS.exec(text)?.[1]?.toLowerCase() ?? null;
}

/** Words a spec uses when it is recording that something was not achieved. */
const UNMET = /\b(UNMET|not met|unmet|deliberately unmet|is not `done`|remains outstanding)\b/;

function specFiles(ctx: CheckContext) {
  return ctx.files.filter((f) => SPEC_FILE.test(f.path.replaceAll("\\", "/")));
}

export const specRequirementWithoutCriterion: Check = {
  ruleId: "spec-requirement-without-criterion",
  describe: "A spec requirement that the Acceptance criteria section never mentions.",
  run(ctx: CheckContext): Promise<Candidate[]> {
    const found: Candidate[] = [];
    for (const file of specFiles(ctx)) {
      const criteria = sectionOf(file.text, "Acceptance criteria");
      // A spec with no acceptance section at all is a different, larger problem, and saying it
      // once per requirement would bury the digest. Leave it to the reading pass.
      if (criteria.trim().length === 0) continue;
      for (const req of requirementsIn(file.text)) {
        if (mentions(criteria, req.id)) continue;
        const line = lineAt(file.text, req.index);
        found.push({
          ruleId: "spec-requirement-without-criterion",
          path: file.path,
          line,
          what: `${req.id} has no acceptance criterion.`,
          why:
            "Nothing states what would demonstrate it, so the acceptance review at the end of " +
            "the spec has nothing to check it against and it will be marked met on judgement " +
            "rather than on evidence.",
          confirm:
            `Open ${file.path}, read '## Acceptance criteria', and search it for ${req.id}: ` +
            "there is no entry.",
          excerpt: excerptAt(file.text, line, 1),
          verified: "verified",
        });
      }
    }
    return Promise.resolve(found);
  },
};

export const specDoneWithOpenTask: Check = {
  ruleId: "spec-done-with-open-task",
  describe: "A spec marked done whose task list still has an unticked task.",
  async run(ctx: CheckContext): Promise<Candidate[]> {
    const found: Candidate[] = [];
    for (const file of specFiles(ctx)) {
      if (statusOf(file.text) !== "done") continue;
      const tasksPath = file.path.replaceAll("\\", "/").replace(/-spec\.md$/, "-tasks.md");
      const tasks = await ctx.readFile(tasksPath);
      if (tasks === null) continue;
      const open = tasks.split(/\r?\n/)
        .map((line, i) => ({ line, no: i + 1 }))
        .filter(({ line }) => /^\s*[-*]\s+\[ \]\s/.test(line));
      if (open.length === 0) continue;

      const statusLine = lineAt(file.text, STATUS.exec(file.text)!.index);
      found.push({
        ruleId: "spec-done-with-open-task",
        path: file.path,
        line: statusLine,
        what:
          `Marked \`Status: done\`, but ${tasksPath} still has ${open.length} unticked ` +
          `task${open.length === 1 ? "" : "s"} (first at line ${open[0].no}).`,
        why:
          "Either work was finished and never ticked, in which case the task list lies about " +
          "what was built, or it was never finished, in which case the status does. Both are " +
          "read later by someone deciding whether this spec can be relied on.",
        confirm: `grep -n '^- \\[ \\]' ${tasksPath}`,
        excerpt: excerptAt(file.text, statusLine, 1),
        verified: "verified",
      });
    }
    return found;
  },
};

export const specDoneWithUnmetRequirement: Check = {
  ruleId: "spec-done-with-unmet-requirement",
  describe: "A spec marked done whose own text records a requirement as unmet.",
  run(ctx: CheckContext): Promise<Candidate[]> {
    const found: Candidate[] = [];
    for (const file of specFiles(ctx)) {
      if (statusOf(file.text) !== "done") continue;
      const lines = file.text.split(/\r?\n/);
      const hit = lines.findIndex((l) => UNMET.test(l));
      if (hit === -1) continue;
      const statusLine = lineAt(file.text, STATUS.exec(file.text)!.index);
      found.push({
        ruleId: "spec-done-with-unmet-requirement",
        path: file.path,
        line: statusLine,
        what: `Marked \`Status: done\`, but line ${hit + 1} records something as unmet.`,
        why:
          "A done spec is the thing later work is built on top of. If a requirement in it was " +
          "not achieved, everything that assumed it was inherits the gap silently.",
        confirm: `Open ${file.path} at line ${hit + 1} and compare it with the Status line.`,
        excerpt: excerptAt(file.text, hit + 1, 1),
        verified: "verified",
      });
    }
    return Promise.resolve(found);
  },
};

export const specTraceabilityChecks: readonly Check[] = [
  specRequirementWithoutCriterion,
  specDoneWithOpenTask,
  specDoneWithUnmetRequirement,
];
