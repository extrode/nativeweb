// What a session reads at triage time.
//
//   R9  the findings that are new since the last triage, once each, and the same list if you
//       run it twice without triaging
//   R11 what the audit is costing, as a trend rather than one number
//   R12 a run that failed, or never happened, is reported as such — never as "no findings"
//
// "New since the last triage" needs no marker file, because triage is what moves a finding out
// of `new`. Anything still `new` has not been triaged, whether it arrived last night or three
// weeks ago. Running the digest twice therefore shows the same list, and triaging a subset
// leaves the rest showing — which is what a person part-way through triage wants.

import { parseArgs } from "jsr:@std/cli@^1.0.0/parse-args";
import { collapse, type Entry, parseLedger, type Verification } from "./ledger.ts";
import { type CoverageSummary, type SliceRow, summarise } from "./slices.ts";
import type { RunRecord } from "./run.ts";

export interface Digest {
  generatedAt: string;
  untriaged: Entry[];
  /** Every finding ever raised, in any state. Distinguishes "none raised" from "all triaged". */
  totalFindings: number;
  coverage: CoverageSummary;
  health: Health;
  cost: CostTrend;
}

export interface Health {
  runs: number;
  failed: RunRecord[];
  bounded: RunRecord[];
  /** Dates between the first and last run with no run record at all (R12). */
  gaps: string[];
  lastRunAt: string | null;
  /** Non-zero here is a defect in the audit itself, not in the repository. */
  corpusLeaks: number;
}

export interface CostTrend {
  runs: number;
  tokensIn: number;
  tokensOut: number;
  costUsd: number | null;
  /** Most recent first, for the sparkline-ish line in the report. */
  recent: Array<{ runId: string; tokensIn: number; tokensOut: number; costUsd: number | null }>;
}

const RANK: Record<Verification, number> = { verified: 0, unverified: 1, refuted: 2 };

/** Verified before unverified (R10), then oldest first so nothing sits at the bottom forever. */
export function rank(entries: readonly Entry[]): Entry[] {
  return [...entries].sort((a, b) => {
    if (RANK[a.verified] !== RANK[b.verified]) return RANK[a.verified] - RANK[b.verified];
    if (a.at !== b.at) return a.at < b.at ? -1 : 1;
    return a.path.localeCompare(b.path) || a.line - b.line;
  });
}

/** Every date from `from` to `to` inclusive, as YYYY-MM-DD. */
export function datesBetween(from: string, to: string): string[] {
  const out: string[] = [];
  const cursor = new Date(`${from}T00:00:00.000Z`);
  const end = new Date(`${to}T00:00:00.000Z`);
  while (cursor.getTime() <= end.getTime()) {
    out.push(cursor.toISOString().slice(0, 10));
    cursor.setUTCDate(cursor.getUTCDate() + 1);
  }
  return out;
}

/**
 * R12. A day with no run record is a gap, and a gap is reported.
 *
 * The alternative — showing only the runs that happened — makes a dead scheduler look exactly
 * like a quiet week, which is the failure this requirement exists to prevent.
 */
export function health(records: readonly RunRecord[], now?: Date): Health {
  const byDate = new Map<string, RunRecord>();
  for (const r of records) byDate.set(r.startedAt.slice(0, 10), r);
  const dates = [...byDate.keys()].sort();

  // The gap window runs to *today*, not to the last run. Measuring only between the first and
  // last run is the one direction that matters getting it backwards: a scheduler that died a
  // month ago produces no run after the last one, so there is nothing between them to be
  // missing, and the digest renders a month of silence as a healthy history.
  const until = now === undefined ? dates[dates.length - 1] : now.toISOString().slice(0, 10);
  const gaps = dates.length === 0
    ? []
    : datesBetween(dates[0], until < dates[dates.length - 1] ? dates[dates.length - 1] : until)
      .filter((d) => !byDate.has(d));

  return {
    runs: records.length,
    failed: records.filter((r) => r.outcome === "failed"),
    bounded: records.filter((r) => r.outcome === "bounded"),
    gaps,
    lastRunAt: records.length === 0
      ? null
      : records.map((r) => r.startedAt).sort().at(-1) ?? null,
    corpusLeaks: records.reduce((n, r) => n + (r.corpusLeaks ?? 0), 0),
  };
}

export function costTrend(records: readonly RunRecord[], recent = 7): CostTrend {
  const sorted = [...records].sort((a, b) => (a.startedAt < b.startedAt ? 1 : -1));
  const priced = sorted.filter((r) => r.costUsd !== null);
  return {
    runs: sorted.length,
    tokensIn: sorted.reduce((n, r) => n + r.tokensIn, 0),
    tokensOut: sorted.reduce((n, r) => n + r.tokensOut, 0),
    costUsd: priced.length === 0 ? null : priced.reduce((n, r) => n + (r.costUsd ?? 0), 0),
    recent: sorted.slice(0, recent).map((r) => ({
      runId: r.runId,
      tokensIn: r.tokensIn,
      tokensOut: r.tokensOut,
      costUsd: r.costUsd,
    })),
  };
}

export function buildDigest(
  ledger: readonly Entry[],
  coverage: readonly SliceRow[],
  records: readonly RunRecord[],
  now: Date,
): Digest {
  const current = [...collapse(ledger).values()];
  return {
    generatedAt: now.toISOString(),
    untriaged: rank(current.filter((e) => e.state === "new")),
    totalFindings: current.length,
    coverage: summarise(coverage),
    health: health(records, now),
    cost: costTrend(records),
  };
}

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

export function render(digest: Digest, repo = "nativeweb"): string {
  const out: string[] = [];
  const h = digest.health;
  const c = digest.coverage;

  out.push(`# Audit digest — ${repo}`);
  out.push(`Generated ${digest.generatedAt}`);
  out.push("");

  // Health first, deliberately. If the scheduler is dead, nothing below this line means what it
  // appears to mean, and a reader who stops after one section should have stopped here.
  if (h.runs === 0) {
    out.push("## Health");
    out.push("");
    out.push("**No run has ever completed.** There are no findings because nothing has looked,");
    out.push("not because the repository is clean.");
    out.push("");
  } else if (h.failed.length > 0 || h.gaps.length > 0 || h.corpusLeaks > 0 || h.bounded.length > 0) {
    out.push("## Health — needs attention");
    out.push("");
    if (h.corpusLeaks > 0) {
      out.push(
        `- **R13 violation: ${plural(h.corpusLeaks, "secret-looking span")} in the written corpus.** ` +
          "This is a defect in the audit, not in the repository. Fix before reading anything else.",
      );
    }
    for (const run of h.failed) {
      out.push(`- **Run \`${run.runId}\` failed:** ${run.error ?? "no error recorded"}`);
    }
    if (h.gaps.length > 0) {
      out.push(
        `- **${plural(h.gaps.length, "day")} with no run at all:** ${h.gaps.join(", ")}. ` +
          "Absence of findings on those days means absence of looking.",
      );
    }
    for (const run of h.bounded) {
      out.push(
        `- Run \`${run.runId}\` stopped at the slice bound on \`${run.slice}\`; that slice was ` +
          "not fully read.",
      );
    }
    out.push("");
  }

  out.push("## Coverage");
  out.push("");
  out.push(
    `${c.audited} of ${c.total} slices audited, ${c.never} never. ` +
      (c.stalest === null
        ? "Nothing has been audited yet."
        : `Stalest: \`${c.stalest.slice}\`, last read ${c.stalest.lastAuditedAt?.slice(0, 10)}.`),
  );
  out.push("");

  out.push(`## Untriaged findings (${digest.untriaged.length})`);
  out.push("");
  if (digest.untriaged.length === 0) {
    // Three different nothings, and conflating them is the failure R12 exists to prevent:
    // nothing ran, nothing was found in what did run, or everything found has been decided.
    if (h.runs === 0) {
      out.push("None, because nothing has run. See Health above.");
    } else if (digest.totalFindings === 0) {
      out.push(
        `None raised. ${c.audited} of ${c.total} slices have been read so far; the rest have ` +
          "not been looked at, so this is not a statement about the repository.",
      );
    } else {
      out.push(`None outstanding. All ${digest.totalFindings} findings so far have been decided.`);
    }
    out.push("");
  } else {
    out.push("| # | Verified | Rule | Where | What |");
    out.push("| --- | --- | --- | --- | --- |");
    digest.untriaged.forEach((e, i) => {
      out.push(
        `| ${i + 1} | ${e.verified} | \`${e.ruleId}\` | \`${e.path}:${e.line}\` | ` +
          `${e.what.replaceAll("|", "\\|")} |`,
      );
    });
    out.push("");
    out.push("### Detail");
    out.push("");
    digest.untriaged.forEach((e, i) => {
      out.push(`**${i + 1}. \`${e.path}:${e.line}\`** — ${e.what}`);
      out.push("");
      out.push(`- Why: ${e.why}`);
      out.push(`- Confirm: \`${e.confirm}\``);
      out.push(`- Id: \`${e.id.slice(0, 12)}\` · rule \`${e.ruleId}\` · first seen ${e.at.slice(0, 10)}`);
      out.push("");
    });
  }

  out.push("## Cost");
  out.push("");
  const cost = digest.cost;
  out.push(
    `${plural(cost.runs, "run")}, ${cost.tokensIn.toLocaleString("en-US")} in / ` +
      `${cost.tokensOut.toLocaleString("en-US")} out. ` +
      (cost.costUsd === null
        ? "No price configured, so no dollar figure is claimed."
        : `$${cost.costUsd.toFixed(4)} total.`),
  );
  if (cost.recent.length > 0) {
    out.push("");
    out.push(
      "Recent: " +
        cost.recent.map((r) => `${r.runId} ${r.tokensIn}/${r.tokensOut}`).join(" · "),
    );
  }
  out.push("");
  out.push("---");
  out.push("");
  out.push("Triage with `tools/audit/triage.ts`. Nothing here has changed the repository, and");
  out.push("nothing will without your say-so.");
  return out.join("\n");
}

// --- loading, locally or over ssh ------------------------------------------------------------

export interface Source {
  readText(path: string): Promise<string | null>;
  listRuns(dir: string): Promise<string[]>;
  /**
   * Why the store could not be read at all, or null if it could. An unreachable host and a store
   * that has never been written are both "no files", and rendering the first as "nothing has ever
   * run, so there are no findings" states a fact about the audit's history on the strength of a
   * failed ssh. R12 in the reader.
   */
  unreachable?(): Promise<string | null>;
}

export function localSource(): Source {
  return {
    async readText(path) {
      try {
        return await Deno.readTextFile(path);
      } catch {
        return null;
      }
    },
    async listRuns(dir) {
      const names: string[] = [];
      try {
        for await (const e of Deno.readDir(dir)) {
          if (e.isFile && e.name.endsWith(".json")) names.push(`${dir}/${e.name}`);
        }
      } catch {
        // No runs directory: reported as "no run has ever completed", not as an error.
      }
      return names.sort();
    },
  };
}

/** Read-only over ssh. Every command here reads; none of them writes on the remote host. */
export function sshSource(host: string): Source {
  const run = async (args: string[]): Promise<string | null> => {
    const { code, stdout } = await new Deno.Command("ssh", {
      args: ["-o", "BatchMode=yes", host, ...args],
      stdout: "piped",
      stderr: "null",
    }).output();
    return code === 0 ? new TextDecoder().decode(stdout) : null;
  };
  return {
    readText: (path) => run(["cat", "--", shellQuote(path)]),
    async listRuns(dir) {
      const out = await run(["ls", "-1", shellQuote(dir)]);
      if (out === null) return [];
      return out.split("\n").filter((n) => n.endsWith(".json")).map((n) => `${dir}/${n}`).sort();
    },
    async unreachable() {
      const { code, stderr } = await new Deno.Command("ssh", {
        args: ["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host, "true"],
        stdout: "null",
        stderr: "piped",
      }).output();
      if (code === 0) return null;
      const detail = new TextDecoder().decode(stderr).trim().split("\n")[0] ?? "";
      return `ssh ${host} failed (${code})${detail.length > 0 ? `: ${detail}` : ""}`;
    },
  };
}

/**
 * Quote a path for a remote shell. `ssh host cat -- <path>` is not argv: ssh joins the arguments
 * and the remote login shell re-splits them, so a path with a space reads the wrong file and one
 * with a metacharacter runs something.
 */
export function shellQuote(value: string): string {
  return `'${value.replaceAll("'", `'\\''`)}'`;
}

export class DigestUnreachable extends Error {
  constructor(reason: string) {
    super(
      `${reason}\n\nThe audit store could not be read, so no digest is produced. This is not ` +
        `"no findings": nothing was looked at.`,
    );
    this.name = "DigestUnreachable";
  }
}

export async function loadDigest(
  source: Source,
  store: string,
  repo: string,
  now: Date,
): Promise<Digest> {
  const cannotReach = source.unreachable !== undefined ? await source.unreachable() : null;
  if (cannotReach !== null) {
    // Not an empty digest. An empty digest is a claim; this is a refusal to make one.
    throw new DigestUnreachable(cannotReach);
  }

  const ledgerText = await source.readText(`${store}/ledger/${repo}.jsonl`);
  const coverageText = await source.readText(`${store}/ledger/${repo}-coverage.json`);
  const runFiles = await source.listRuns(`${store}/runs`);

  const records: RunRecord[] = [];
  for (const file of runFiles) {
    const text = await source.readText(file);
    if (text === null) continue;
    try {
      records.push(JSON.parse(text) as RunRecord);
    } catch {
      console.error(`digest: skipping unreadable run record ${file}`);
    }
  }

  let coverage: SliceRow[] = [];
  if (coverageText !== null) {
    try {
      const parsed = JSON.parse(coverageText);
      if (Array.isArray(parsed)) coverage = parsed as SliceRow[];
    } catch {
      console.error("digest: coverage file is unreadable");
    }
  }

  return buildDigest(parseLedger(ledgerText ?? ""), coverage, records, now);
}

if (import.meta.main) {
  const args = parseArgs(Deno.args, {
    string: ["store", "ssh", "repo"],
    boolean: ["json", "help"],
    default: { store: `${Deno.env.get("HOME") ?? "."}/audit`, repo: "nativeweb" },
  });

  if (args.help) {
    console.log(`Usage: deno run -A digest.ts [--ssh <host>] [--store <path>] [--json]

  --ssh <host>    read the store on that host over ssh (reads only; writes nothing there)
  --store <path>  store location, on whichever host (default ~/audit)
  --repo <name>   which ledger to read (default nativeweb)
  --json          emit the digest as JSON instead of Markdown`);
    Deno.exit(0);
  }

  const source = args.ssh !== undefined ? sshSource(args.ssh) : localSource();
  try {
    const digest = await loadDigest(source, args.store, args.repo, new Date());
    console.log(args.json ? JSON.stringify(digest, null, 2) : render(digest, args.repo));
  } catch (cause) {
    if (cause instanceof DigestUnreachable) {
      console.error(cause.message);
      Deno.exit(4);
    }
    throw cause;
  }
}
