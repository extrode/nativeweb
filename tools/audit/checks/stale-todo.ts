// A TODO that is both old and unattributed.
//
// Neither half is a defect alone. A TODO written yesterday is a note to self. A TODO carrying
// `TODO(sy)` or `TODO: #412` is tracked, however old — someone owns it and it can be chased.
// The pair is the defect: a marker nobody owns, on code that has moved on around it, recording
// an intent that may no longer be anybody's intent.
//
// Both halves have to be true, because reporting every TODO is how an audit becomes noise on
// day three and gets ignored on day four.

import type { Candidate } from "../ledger.ts";
import { type Check, type CheckContext, excerptAt } from "./types.ts";

/** How old, in days, before an unattributed marker is worth a person's attention. */
export const DEFAULT_AGE_DAYS = 180;

const MARKER = /\b(TODO|FIXME|HACK|XXX)\b([^\n]*)/g;

/** An owner in parentheses, an issue number, a URL, or a spec id all count as attribution. */
const ATTRIBUTED = [
  /^\s*\(/, //             TODO(sy)
  /#\d+/, //               TODO: #412
  /https?:\/\//, //        TODO: see https://github.com/dotnet/aspnetcore/issues/1
  /\bspec\s*\d{3}\b/i, //  TODO: spec 011
  /\bR\d+\b/, //           TODO: R14
  /\b\d{4}-\d{2}-\d{2}\b/, // TODO by 2026-12-01
];

export function isAttributed(tail: string): boolean {
  return ATTRIBUTED.some((re) => re.test(tail));
}

export function makeStaleTodo(ageDays = DEFAULT_AGE_DAYS): Check {
  return {
    ruleId: "stale-todo",
    describe:
      `A TODO/FIXME/HACK/XXX marker older than ${ageDays} days with no owner, issue or date.`,
    async run(ctx: CheckContext): Promise<Candidate[]> {
      const found: Candidate[] = [];
      const cutoffMs = ctx.now.getTime() - ageDays * 24 * 60 * 60 * 1000;

      for (const file of ctx.files) {
        const lines = file.text.split(/\r?\n/);
        for (let i = 0; i < lines.length; i++) {
          const re = new RegExp(MARKER.source, "g");
          let m: RegExpExecArray | null;
          while ((m = re.exec(lines[i])) !== null) {
            const [, kind, tail] = m;
            if (isAttributed(tail)) continue;

            const line = i + 1;
            const blame = await ctx.blame(file.path, line);
            // R6: without an age we cannot claim staleness, so we do not raise the finding.
            // Silence beats a claim the ledger cannot support.
            if (blame === null || blame.authorTime.getTime() > cutoffMs) continue;

            const days = Math.floor((ctx.now.getTime() - blame.authorTime.getTime()) / 86_400_000);
            found.push({
              ruleId: "stale-todo",
              path: file.path,
              line,
              what:
                `${kind} marker untouched for ${days} days, with no owner, issue number or date.`,
              why:
                "It records an intent nobody is tracking. The surrounding code has had " +
                `${days} days to move away from whatever the marker assumed, so the note is as ` +
                "likely to mislead a reader as to help one.",
              confirm:
                `git blame -L ${line},${line} -- ${file.path}  # last touched ` +
                `${blame.authorTime.toISOString().slice(0, 10)} by ${blame.authorName}`,
              excerpt: excerptAt(file.text, line, 2),
              verified: "verified",
            });
          }
        }
      }
      return found;
    },
  };
}

export const staleTodo: Check = makeStaleTodo();
