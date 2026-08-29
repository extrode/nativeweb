// Applying a decision to a finding.
//
//   R8  every state change carries a dated reason, and `rejected` is absorbing
//   R14 acting on a finding requires the user's agreement
//
// On what `--approved-by` is and is not. It is a **record**, not an enforcement boundary: an
// agent with a shell can type any flag, and pretending otherwise would be worse than saying so.
// The boundary that does hold is structural — nothing in the audit path calls this module. The
// audit writes findings; it never decides about them. `--approved-by` exists so that when a
// branch is opened against an accepted finding, the ledger says who agreed and when, and the
// answer is not "the tooling assumed you did".
//
// Rejection needs no approval token, and that asymmetry is deliberate. Declining to act costs
// nothing and un-declining is one more line; accepting is what leads to a branch and a diff.

import { parseArgs } from "jsr:@std/cli@^1.0.0/parse-args";
import {
  collapse,
  type Entry,
  parseLedger,
  serialise,
  type State,
  transition,
  TransitionError,
} from "./ledger.ts";
import { shellQuote } from "./digest.ts";

export const DECIDABLE: readonly State[] = ["accepted", "rejected", "fixed"];

export interface Decision {
  /** A finding id, or any unambiguous prefix of one. */
  id: string;
  state: State;
  reason: string;
  /** Required for `accepted` (R14). Recorded verbatim. */
  approvedBy?: string;
}

export interface Applied {
  applied: Entry[];
  refused: Array<{ id: string; why: string }>;
}

export class TriageError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TriageError";
  }
}

/**
 * Resolve an id prefix against the ledger.
 *
 * A digest shows a 12-character prefix because a 64-character hash is unreadable, so triage has
 * to accept one. An ambiguous prefix is an error rather than a guess: picking the first match
 * would apply the user's decision to a finding they were not looking at.
 */
export function resolveId(prefix: string, ids: Iterable<string>): string {
  const clean = prefix.trim().toLowerCase();
  if (clean.length < 6) {
    throw new TriageError(`id prefix '${prefix}' is too short to be unambiguous (6 minimum)`);
  }
  const matches = [...ids].filter((id) => id.toLowerCase().startsWith(clean));
  if (matches.length === 0) throw new TriageError(`no finding matches id '${prefix}'`);
  if (matches.length > 1) {
    throw new TriageError(
      `id '${prefix}' matches ${matches.length} findings: ${matches.map((m) => m.slice(0, 16)).join(", ")}`,
    );
  }
  return matches[0];
}

/** R14: `accepted` without a recorded approver is refused before anything is written. */
export function checkApproval(decision: Decision): string | null {
  if (decision.state !== "accepted") return null;
  const who = (decision.approvedBy ?? "").trim();
  if (who.length === 0) {
    return "accepting a finding records who agreed to act on it; pass --approved-by (R14)";
  }
  return null;
}

export function validateDecision(decision: Decision): string | null {
  if (!DECIDABLE.includes(decision.state)) {
    return `'${decision.state}' is not a decision; use one of ${DECIDABLE.join(", ")}`;
  }
  if (decision.reason.trim().length === 0) return "a reason is required (R8)";
  return checkApproval(decision);
}

/**
 * Build the records for a batch of decisions against the current ledger.
 *
 * Nothing is written here, and a refused decision refuses only itself: the valid decisions in a
 * batch are applied and the invalid ones are reported, so a batch that exits non-zero has still
 * changed the ledger. That is the right trade for an append-only ledger — re-running the batch
 * re-applies what already landed as a no-op — but it does mean "refused" is not "nothing
 * happened". `triage` returns both lists and the CLI prints both.
 */
export function applyDecisions(
  ledger: readonly Entry[],
  decisions: readonly Decision[],
  runId: string,
  now: Date,
): Applied {
  const current = collapse(ledger);
  const applied: Entry[] = [];
  const refused: Array<{ id: string; why: string }> = [];
  // Later decisions in the same batch see earlier ones, so `accept then fix` in one run works.
  const staged = new Map(current);

  for (const decision of decisions) {
    const invalid = validateDecision(decision);
    if (invalid !== null) {
      refused.push({ id: decision.id, why: invalid });
      continue;
    }
    let id: string;
    try {
      id = resolveId(decision.id, staged.keys());
    } catch (cause) {
      refused.push({ id: decision.id, why: (cause as Error).message });
      continue;
    }
    const entry = staged.get(id)!;
    try {
      const reason = decision.state === "accepted"
        ? `${decision.reason.trim()} [approved-by: ${decision.approvedBy!.trim()}]`
        : decision.reason.trim();
      const next = transition(entry, decision.state, reason, runId, now);
      staged.set(id, next);
      applied.push(next);
    } catch (cause) {
      refused.push({
        id: id.slice(0, 12),
        why: cause instanceof TransitionError ? cause.message : String(cause),
      });
    }
  }
  return { applied, refused };
}

// --- stores ------------------------------------------------------------------------------------

export interface LedgerStore {
  read(): Promise<string>;
  append(text: string): Promise<void>;
}

export function localStore(path: string): LedgerStore {
  return {
    async read() {
      try {
        return await Deno.readTextFile(path);
      } catch {
        return "";
      }
    },
    async append(text) {
      await Deno.writeTextFile(path, text, { append: true, create: true });
    },
  };
}

/**
 * The ledger of record lives on the audit host, so triage from the workstation writes there.
 *
 * Appending is the only write, which is what makes doing it over a pipe safe enough: the file is
 * append-only by design, so a concurrent nightly run adding findings and a session adding
 * decisions cannot lose each other's lines the way a read-modify-write would.
 */
export function sshStore(host: string, path: string): LedgerStore {
  return {
    async read() {
      const { code, stdout, stderr } = await new Deno.Command("ssh", {
        args: ["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host, "cat", "--", shellQuote(path)],
        stdout: "piped",
        stderr: "piped",
      }).output();
      // 255 is ssh's own failure, not the remote command's. Treating it as an empty ledger would
      // turn an unreachable host into "no finding matches that id" — and the user would go
      // looking for the id rather than for the network.
      if (code === 255) {
        throw new TriageError(
          `cannot reach ${host}: ${new TextDecoder().decode(stderr).trim().split("\n")[0] ?? ""}`,
        );
      }
      return code === 0 ? new TextDecoder().decode(stdout) : "";
    },
    async append(text) {
      const child = new Deno.Command("ssh", {
        args: ["-o", "BatchMode=yes", host, "cat", ">>", shellQuote(path)],
        stdin: "piped",
        stdout: "null",
        stderr: "piped",
      }).spawn();
      const writer = child.stdin.getWriter();
      await writer.write(new TextEncoder().encode(text));
      await writer.close();
      const { code, stderr } = await child.output();
      if (code !== 0) {
        throw new TriageError(`ssh append failed (${code}): ${new TextDecoder().decode(stderr)}`);
      }
    },
  };
}

export async function triage(
  store: LedgerStore,
  decisions: readonly Decision[],
  runId: string,
  now: Date,
): Promise<Applied> {
  const result = applyDecisions(parseLedger(await store.read()), decisions, runId, now);
  if (result.applied.length > 0) await store.append(serialise(result.applied));
  return result;
}

// --- CLI ----------------------------------------------------------------------------------------

export function decisionsFromArgs(args: {
  id?: string;
  state?: string;
  reason?: string;
  "approved-by"?: string;
}): Decision[] {
  if (args.id === undefined) return [];
  return [{
    id: args.id,
    state: (args.state ?? "") as State,
    reason: args.reason ?? "",
    approvedBy: args["approved-by"],
  }];
}

if (import.meta.main) {
  const args = parseArgs(Deno.args, {
    string: ["store", "ssh", "repo", "id", "state", "reason", "approved-by", "from", "run-id"],
    boolean: ["help"],
    default: { store: `${Deno.env.get("HOME") ?? "."}/audit`, repo: "nativeweb" },
  });

  if (args.help || (args.id === undefined && args.from === undefined)) {
    console.log(`Usage: deno run -A triage.ts --id <id> --state <state> --reason "<why>" [...]

  --id <prefix>        finding id, or any unambiguous prefix of at least 6 characters
  --state <state>      ${DECIDABLE.join(" | ")}
  --reason "<why>"     required, recorded with the decision (R8)
  --approved-by <who>  required for 'accepted' — recorded, not enforced (R14)
  --from <file>        a JSON array of decisions instead of one on the command line
  --ssh <host>         apply against the ledger on that host
  --store <path>       store location (default ~/audit)
  --repo <name>        which ledger (default nativeweb)

Rejecting needs no approver: declining to act is reversible with one more line, and
'rejected' is absorbing, so a dismissed finding never returns to the digest.`);
    Deno.exit(args.help ? 0 : 1);
  }

  const decisions: Decision[] = args.from !== undefined
    ? JSON.parse(await Deno.readTextFile(args.from)) as Decision[]
    : decisionsFromArgs(args);

  const path = `${args.store}/ledger/${args.repo}.jsonl`;
  const store = args.ssh !== undefined ? sshStore(args.ssh, path) : localStore(path);
  const runId = args["run-id"] ?? `triage-${new Date().toISOString().replace(/[:.]/g, "-")}`;

  const result = await triage(store, decisions, runId, new Date());
  for (const entry of result.applied) {
    console.log(`${entry.state.padEnd(8)} ${entry.id.slice(0, 12)}  ${entry.path}:${entry.line}`);
  }
  for (const refusal of result.refused) {
    console.error(`refused ${refusal.id}: ${refusal.why}`);
  }
  console.log(`${result.applied.length} applied, ${result.refused.length} refused`);
  Deno.exit(result.refused.length > 0 ? 1 : 0);
}
