// The append-only finding ledger.
//
// Three properties are the whole point of this module, and each is a spec requirement rather
// than a preference:
//
//   R6  a finding that cannot be acted on without re-deriving it never enters the ledger
//   R7  a finding's identity survives its line number moving
//   R8  every transition carries a date and a reason, and `rejected` is absorbing
//
// Nothing here mutates a line once written. The current state of a finding is the last line
// bearing its id, so a corrupt write costs one record rather than the file, and the history of
// a decision stays readable.

export type Verification = "verified" | "unverified" | "refuted";
export type State = "new" | "accepted" | "rejected" | "fixed";

/** What a check produces. Not yet a ledger entry: it has no identity and no state. */
export interface Candidate {
  ruleId: string;
  path: string;
  line: number;
  /** What is wrong. */
  what: string;
  /** Why it matters. */
  why: string;
  /** How a reader confirms or refutes it. */
  confirm: string;
  /** The code the finding concerns. Identity is derived from this, never from `line`. */
  excerpt: string;
  verified: Verification;
}

export interface Entry {
  id: string;
  ruleId: string;
  path: string;
  line: number;
  what: string;
  why: string;
  confirm: string;
  verified: Verification;
  state: State;
  /** ISO-8601. */
  at: string;
  /** Non-empty on every record, including the first. */
  reason: string;
  runId: string;
}

export interface Dropped {
  candidate: Candidate;
  reason: string;
}

export interface AdmitResult {
  /** Candidates not already in the ledger, as fresh `new` entries. */
  admitted: Entry[];
  /** Line-moved records for findings already present whose line changed. */
  moved: Entry[];
  /** Ids already present and unchanged. Reported nowhere; this is R7 doing its job. */
  unchanged: string[];
  /** Candidates that failed validation, with the reason each failed. */
  dropped: Dropped[];
}

/**
 * Collapse a code excerpt to the form identity is computed over.
 *
 * Strips per-line indentation and collapses internal whitespace runs, so that reformatting or
 * an added import above a defect does not resurrect it as a new finding. Blank lines go
 * entirely, for the same reason.
 */
export function normaliseExcerpt(excerpt: string): string {
  return excerpt
    .split(/\r?\n/)
    .map((l) => l.trim().replace(/\s+/g, " "))
    .filter((l) => l.length > 0)
    .join("\n");
}

const encoder = new TextEncoder();

/** SHA-256 over path, rule and normalised excerpt. The line number is deliberately absent. */
export async function computeId(
  input: Pick<Candidate, "path" | "ruleId" | "excerpt">,
): Promise<string> {
  const material = [
    input.path.replaceAll("\\", "/"),
    input.ruleId,
    normaliseExcerpt(input.excerpt),
  ].join("\0");
  const digest = await crypto.subtle.digest("SHA-256", encoder.encode(material));
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/** R6. Returns the reason a candidate is unusable, or null when it is usable. */
export function validate(candidate: Candidate): string | null {
  const nonEmpty = (v: unknown) => typeof v === "string" && v.trim().length > 0;
  if (!nonEmpty(candidate.ruleId)) return "missing ruleId";
  if (!nonEmpty(candidate.path)) return "missing path";
  if (!nonEmpty(candidate.what)) return "missing what: nothing states the defect";
  if (!nonEmpty(candidate.why)) return "missing why: nothing states why it matters";
  if (!nonEmpty(candidate.confirm)) return "missing confirm: no way to check the claim";
  if (!nonEmpty(candidate.excerpt)) return "missing excerpt: identity cannot be computed";
  if (!Number.isInteger(candidate.line) || candidate.line < 1) {
    return `line is not a positive integer (${candidate.line})`;
  }
  return null;
}

const TRANSITIONS: Record<State, readonly State[]> = {
  // A fresh finding can go anywhere a decision takes it.
  new: ["accepted", "rejected", "fixed"],
  // Accepted work can complete, or be reconsidered and dropped.
  accepted: ["fixed", "rejected"],
  // A regression re-opens a fixed finding. This is the only way back to `new`.
  fixed: ["new", "accepted", "rejected"],
  // R8: absorbing. Disagreeing with the audit costs one decision, not one per day forever.
  rejected: [],
};

export function canTransition(from: State, to: State): boolean {
  return TRANSITIONS[from].includes(to);
}

export class TransitionError extends Error {
  constructor(readonly id: string, readonly from: State, readonly to: State, why: string) {
    super(`${id}: ${from} -> ${to} refused: ${why}`);
    this.name = "TransitionError";
  }
}

/** Build the record for a state change. Throws rather than writing an illegal transition. */
export function transition(
  current: Entry,
  to: State,
  reason: string,
  runId: string,
  now: Date,
): Entry {
  if (reason.trim().length === 0) {
    throw new TransitionError(current.id, current.state, to, "a reason is required (R8)");
  }
  if (current.state === "rejected") {
    throw new TransitionError(
      current.id,
      current.state,
      to,
      "rejected is absorbing; raise it under a new rule if it genuinely recurs",
    );
  }
  if (!canTransition(current.state, to)) {
    throw new TransitionError(current.id, current.state, to, "not a permitted transition");
  }
  return { ...current, state: to, at: now.toISOString(), reason, runId };
}

/**
 * Parse a JSONL ledger into the current state of every finding. Last record for an id wins,
 * except that `rejected` is absorbing on the way in as well as on the way out.
 *
 * `transition` already refuses to *write* a record that leaves `rejected`, so on a ledger this
 * process produced the exception never fires. It fires on a ledger that was hand-edited, merged,
 * or concatenated from two hosts — and there the reader is the last line of defence for R8. A
 * finding the maintainer dismissed must not reappear in tomorrow's digest because a stray line
 * landed after the dismissal.
 */
export function collapse(lines: Iterable<Entry>): Map<string, Entry> {
  const current = new Map<string, Entry>();
  for (const entry of lines) {
    if (current.get(entry.id)?.state === "rejected") continue;
    current.set(entry.id, entry);
  }
  return current;
}

const STATES: readonly string[] = ["new", "accepted", "rejected", "fixed"];
const VERIFICATIONS: readonly string[] = ["verified", "unverified", "refuted"];

/** Null if this parsed value is a usable `Entry`, otherwise what is wrong with it. */
export function shapeError(value: unknown): string | null {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return "not an object";
  }
  const e = value as Record<string, unknown>;
  for (const field of ["id", "ruleId", "path", "what", "why", "confirm", "at"]) {
    if (typeof e[field] !== "string" || (e[field] as string).length === 0) {
      return `missing or empty '${field}'`;
    }
  }
  if (typeof e.line !== "number" || !Number.isFinite(e.line)) return "missing or non-numeric 'line'";
  if (!STATES.includes(e.state as string)) return `'state' is not one of ${STATES.join(", ")}`;
  if (!VERIFICATIONS.includes(e.verified as string)) {
    return `'verified' is not one of ${VERIFICATIONS.join(", ")}`;
  }
  return null;
}

export function parseLedger(text: string): Entry[] {
  const entries: Entry[] = [];
  let lineNo = 0;
  for (const raw of text.split(/\r?\n/)) {
    lineNo++;
    const line = raw.trim();
    if (line.length === 0) continue;
    try {
      const parsed = JSON.parse(line);
      // Shape-check, not just parse-check. This module is documented as the last line of defence
      // for a hand-edited or concatenated ledger, and a record with a valid `id` but no `what`
      // reaches the digest's renderer and throws there — a bad line in the ledger taking out the
      // whole report. Reject it here and say which line.
      const bad = shapeError(parsed);
      if (bad !== null) {
        console.error(`ledger: skipping malformed record at line ${lineNo}: ${bad}`);
        continue;
      }
      entries.push(parsed as Entry);
    } catch (cause) {
      // One bad record must not cost the file. Say which, and carry on.
      console.error(`ledger: skipping unparseable record at line ${lineNo}: ${cause}`);
    }
  }
  return entries;
}

/**
 * Decide what each candidate is against the ledger as it stands.
 *
 * Already present and at the same line: unchanged, reported nowhere (R7).
 * Already present at a different line: a `line moved` record, state untouched.
 * Not present: a fresh `new` entry.
 */
export async function admit(
  candidates: readonly Candidate[],
  current: Map<string, Entry>,
  runId: string,
  now: Date,
): Promise<AdmitResult> {
  const result: AdmitResult = { admitted: [], moved: [], unchanged: [], dropped: [] };
  const seen = new Set<string>();

  for (const candidate of candidates) {
    const invalid = validate(candidate);
    if (invalid !== null) {
      result.dropped.push({ candidate, reason: invalid });
      continue;
    }
    const id = await computeId(candidate);
    if (seen.has(id)) {
      result.dropped.push({ candidate, reason: "duplicate within the same run" });
      continue;
    }
    seen.add(id);

    const existing = current.get(id);
    if (existing === undefined) {
      result.admitted.push({
        id,
        ruleId: candidate.ruleId,
        path: candidate.path.replaceAll("\\", "/"),
        line: candidate.line,
        what: candidate.what,
        why: candidate.why,
        confirm: candidate.confirm,
        verified: candidate.verified,
        state: "new",
        at: now.toISOString(),
        reason: "first seen",
        runId,
      });
      continue;
    }
    if (existing.state === "rejected") {
      // R8. Not even a line move is worth a record on something the user has dismissed.
      result.unchanged.push(id);
      continue;
    }
    if (existing.line !== candidate.line) {
      result.moved.push({
        ...existing,
        line: candidate.line,
        at: now.toISOString(),
        reason: `line moved ${existing.line} -> ${candidate.line}`,
        runId,
      });
      continue;
    }
    result.unchanged.push(id);
  }
  return result;
}

export function serialise(entries: readonly Entry[]): string {
  if (entries.length === 0) return "";
  return entries.map((e) => JSON.stringify(e)).join("\n") + "\n";
}

export async function readLedger(path: string): Promise<Entry[]> {
  try {
    return parseLedger(await Deno.readTextFile(path));
  } catch (cause) {
    if (cause instanceof Deno.errors.NotFound) return [];
    throw cause;
  }
}

/** Append-only by construction: there is no code path here that opens the file for truncation. */
export async function appendLedger(path: string, entries: readonly Entry[]): Promise<void> {
  const payload = serialise(entries);
  if (payload.length === 0) return;
  await Deno.writeTextFile(path, payload, { append: true, create: true });
}
