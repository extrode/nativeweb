// The one part of a run that needs a model: read a slice nobody has reopened, and say what is
// wrong with it. Then try to prove each answer wrong before it reaches a person.
//
// R3  the slice is bounded; exceeding the bound ends the run rather than continuing
// R10 a finding reaching the digest has been checked by something other than what produced it
// R11 what the pass cost is recorded
//
// The refutation pass is not caution. It is a measured response: on this project a glm-5.3
// worker once reported a ReferenceError that one shell command disproved, and glm-4.7 misread a
// regex the same way. A confidently wrong finding costs more attention than the finding saved.

import type { Candidate } from "./ledger.ts";
import { complete, extractJsonArray, type ModelOptions, type Usage } from "./model.ts";
import type { SourceFile } from "./checks/types.ts";
import { excerptAt } from "./checks/types.ts";

/** Bytes of slice text a single run will read. Exceeding it ends the run (R3). */
export const DEFAULT_SLICE_BYTE_BOUND = 120_000;

export interface ReadingPassOptions extends ModelOptions {
  sliceByteBound?: number;
}

export interface ReadingPassResult {
  candidates: Candidate[];
  /** How many the refutation pass knocked down. Reported, never silent. */
  refuted: number;
  /** How many the refuter could not evaluate, and which are therefore `unverified`. */
  unevaluated: number;
  /** Candidates the model produced that did not survive validation against the real file. */
  discarded: number;
  usage: Usage;
  bounded: boolean;
  /** Set when the slice was read but the model ran out of room mid-answer. */
  truncated: boolean;
}

const READER_SYSTEM = `You are auditing one directory of a mature codebase. Nobody has reopened
this code recently; assume it compiles, its tests pass, and it was reviewed once when it merged.
You are looking for what a second reading finds: a case the code does not handle, state that can
desynchronise, an error path that swallows, a comment that no longer describes the code, a guard
that can be bypassed, a resource not released.

Rules you must follow:
- Report only defects you can point at a specific line for.
- Do not report style, formatting, naming, or missing comments.
- Do not report anything you would have to guess about code you cannot see.
- If the directory looks sound, return an empty array. That is a normal and useful answer.
- Never include a password, key, token or connection string value in your output.

Return ONLY a JSON array. Each element:
{"line": <integer>, "file": "<one of the files listed>", "what": "<one sentence: the defect>",
 "why": "<one or two sentences: what goes wrong as a result>",
 "confirm": "<a command or a concrete reading a person can use to check this>"}`;

const REFUTER_SYSTEM = `You are given a claim about a specific line of code, and the code around
it. Your job is to REFUTE the claim, not to assess it.

Show that the claim is wrong: the code does handle that case, the guard is elsewhere, the API
does not behave as the claim assumes, the path is unreachable. If you cannot demonstrate that
the claim is wrong, and you also cannot demonstrate that it is right, say so.

Default to refuted when you are uncertain. A claim that survives you is going to take up a
person's attention, so the bar for surviving is that you tried to break it and could not.

Return ONLY JSON: {"refuted": true|false, "reason": "<one sentence>"}`;

export function sliceBytes(files: readonly SourceFile[]): number {
  return files.reduce((total, f) => total + f.text.length, 0);
}

/** Line-numbered so the model can cite a line and we can check the citation. */
export function buildReaderPrompt(slice: string, files: readonly SourceFile[]): string {
  const parts = [`Directory under audit: ${slice}`, ""];
  for (const file of files) {
    parts.push(`--- ${file.path} ---`);
    file.text.split(/\r?\n/).forEach((line, i) => parts.push(`${i + 1}\t${line}`));
    parts.push("");
  }
  return parts.join("\n");
}

export function buildRefuterPrompt(candidate: Candidate, file: SourceFile): string {
  const from = Math.max(1, candidate.line - 25);
  return [
    `Claim about ${candidate.path}:${candidate.line}`,
    `  what: ${candidate.what}`,
    `  why:  ${candidate.why}`,
    "",
    `Code from ${file.path}, starting at line ${from}:`,
    excerptAt(file.text, from, 60),
  ].join("\n");
}

interface RawFinding {
  line?: unknown;
  file?: unknown;
  what?: unknown;
  why?: unknown;
  confirm?: unknown;
}

/**
 * Turn what the model said into candidates, discarding anything it cannot support.
 *
 * The excerpt is taken from the real file at the cited line, never from the model's output.
 * That way identity (R7) is computed over code that exists, and a model that paraphrases the
 * code it is describing cannot make the same finding hash differently on two runs.
 */
export function toCandidates(
  raw: readonly unknown[],
  files: readonly SourceFile[],
): { candidates: Candidate[]; discarded: number } {
  const byPath = new Map(files.map((f) => [f.path, f]));
  const candidates: Candidate[] = [];
  let discarded = 0;

  for (const item of raw) {
    if (typeof item !== "object" || item === null) {
      discarded++;
      continue;
    }
    const { line, file, what, why, confirm } = item as RawFinding;
    const source = typeof file === "string"
      ? byPath.get(file) ?? [...byPath.values()].find((f) => f.path.endsWith(file))
      : files.length === 1
      ? files[0]
      : undefined;
    const lineCount = source === undefined ? 0 : source.text.split(/\r?\n/).length;

    if (
      source === undefined ||
      typeof line !== "number" || !Number.isInteger(line) || line < 1 || line > lineCount ||
      typeof what !== "string" || what.trim().length === 0 ||
      typeof why !== "string" || why.trim().length === 0 ||
      typeof confirm !== "string" || confirm.trim().length === 0
    ) {
      discarded++;
      continue;
    }
    candidates.push({
      ruleId: "reading-pass",
      path: source.path,
      line,
      what: what.trim(),
      why: why.trim(),
      confirm: confirm.trim(),
      excerpt: excerptAt(source.text, line, 3),
      verified: "unverified",
    });
  }
  return { candidates, discarded };
}

export function parseVerdict(text: string): { refuted: boolean; reason: string } | null {
  const start = text.indexOf("{");
  const end = text.lastIndexOf("}");
  if (start === -1 || end <= start) return null;
  try {
    const parsed = JSON.parse(text.slice(start, end + 1)) as { refuted?: unknown; reason?: unknown };
    if (typeof parsed.refuted !== "boolean") return null;
    return {
      refuted: parsed.refuted,
      reason: typeof parsed.reason === "string" ? parsed.reason : "",
    };
  } catch {
    return null;
  }
}

const EMPTY: Usage = { inputTokens: 0, outputTokens: 0 };

function add(a: Usage, b: Usage): Usage {
  return {
    inputTokens: a.inputTokens + b.inputTokens,
    outputTokens: a.outputTokens + b.outputTokens,
  };
}

export async function readingPass(
  slice: string,
  files: readonly SourceFile[],
  options: ReadingPassOptions,
): Promise<ReadingPassResult> {
  const bound = options.sliceByteBound ?? DEFAULT_SLICE_BYTE_BOUND;
  const empty: ReadingPassResult = {
    candidates: [],
    refuted: 0,
    unevaluated: 0,
    discarded: 0,
    usage: EMPTY,
    bounded: false,
    truncated: false,
  };

  // R3. A slice too large to read is not read partially: a partial read reports a partial
  // answer as if it were a whole one, and the coverage table would then claim the slice done.
  if (sliceBytes(files) > bound) return { ...empty, bounded: true };
  if (files.length === 0) return empty;

  const read = await complete(READER_SYSTEM, buildReaderPrompt(slice, files), options);
  let usage = read.usage;

  const raw = extractJsonArray(read.text);
  if (raw === null) {
    // Not an error: a model that answered in prose has told us nothing usable, and saying so
    // is more honest than inventing findings from its sentences.
    return { ...empty, usage, truncated: read.truncated };
  }

  const { candidates, discarded } = toCandidates(raw, files);
  const byPath = new Map(files.map((f) => [f.path, f]));

  const survivors: Candidate[] = [];
  let refuted = 0;
  let unevaluated = 0;

  for (const candidate of candidates) {
    const file = byPath.get(candidate.path)!;
    let verdict: { refuted: boolean; reason: string } | null = null;
    try {
      const check = await complete(REFUTER_SYSTEM, buildRefuterPrompt(candidate, file), options);
      usage = add(usage, check.usage);
      verdict = parseVerdict(check.text);
    } catch {
      verdict = null; // R10: could not be checked, not "checked and fine".
    }

    if (verdict === null) {
      unevaluated++;
      survivors.push({ ...candidate, verified: "unverified" });
      continue;
    }
    if (verdict.refuted) {
      refuted++;
      continue;
    }
    survivors.push({ ...candidate, verified: "verified" });
  }

  return {
    candidates: survivors,
    refuted,
    unevaluated,
    discarded,
    usage,
    bounded: false,
    truncated: read.truncated,
  };
}
