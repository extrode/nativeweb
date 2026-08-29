// The contract every mechanical check implements.
//
// A check is a pure function of the slice's files plus a small, injectable set of facts it
// cannot compute itself (the clock, and git). Injectable because a check that shells out
// unconditionally cannot be tested without a repository shaped like the defect it is looking
// for, and a check nobody can test is a check nobody can trust.

import type { Candidate } from "../ledger.ts";

export interface SourceFile {
  /** Repository-relative, forward slashes. */
  path: string;
  text: string;
}

/** One blamed line: who last touched it and when. */
export interface Blame {
  authorTime: Date;
  authorName: string;
}

export interface CheckContext {
  slice: string;
  /** The files in the slice under audit. */
  files: readonly SourceFile[];
  /** Every tracked path in the repository, for checks that look outside the slice. */
  allPaths: ReadonlySet<string>;
  now: Date;
  /** Last-modification facts for a line. Returns null when git cannot answer. */
  blame(path: string, line: number): Promise<Blame | null>;
  /**
   * Read a tracked file outside the slice. Returns null when it does not exist.
   *
   * A check that has to compare two documents — a spec against its task list — cannot work from
   * `files` alone, and `allPaths` carries names without contents.
   */
  readFile(path: string): Promise<string | null>;
}

export interface Check {
  ruleId: string;
  /** One line, shown in the digest's rule legend. */
  describe: string;
  run(ctx: CheckContext): Promise<Candidate[]>;
}

/** Lines `line` through `line + span - 1`, 1-based, for use as a finding's excerpt. */
export function excerptAt(text: string, line: number, span = 2): string {
  const lines = text.split(/\r?\n/);
  return lines.slice(Math.max(0, line - 1), Math.max(0, line - 1) + span).join("\n");
}

/** The 1-based line number containing character offset `index`. */
export function lineAt(text: string, index: number): number {
  let line = 1;
  for (let i = 0; i < index && i < text.length; i++) {
    if (text[i] === "\n") line++;
  }
  return line;
}

/** A blame that always declines, for tests and for the case where git is unavailable. */
export const noBlame: CheckContext["blame"] = () => Promise.resolve(null);

/** A reader that finds nothing, for checks under test that do not look outside their slice. */
export const noReadFile: CheckContext["readFile"] = () => Promise.resolve(null);
