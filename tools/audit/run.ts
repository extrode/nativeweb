// One audit run. Executed on the audit host by launchd, once a day.
//
//   R1  the repository under audit is a throwaway clone, deleted whether the run succeeds or
//       throws. Nothing here can write to the source of truth, because nothing here can reach it.
//   R3  one bounded slice per run
//   R5  the slice is the least recently audited one
//   R11 duration and token cost are recorded
//   R12 a run that throws still writes a record saying so, so absence is never silent
//   R13 every candidate is redacted before it is hashed or written

import { parseArgs } from "jsr:@std/cli@^1.0.0/parse-args";
import { admit, appendLedger, type Candidate, collapse, readLedger } from "./ledger.ts";
import { redactCandidate, scanCorpus } from "./redact.ts";
import {
  enumerateSlices,
  mergeCoverage,
  readCoverage,
  recordAudited,
  selectSlice,
  trackedFiles,
  writeCoverage,
} from "./slices.ts";
import type { Blame, Check, CheckContext, SourceFile } from "./checks/types.ts";
import { staleTodo } from "./checks/stale-todo.ts";
import { docNamesMissingFile } from "./checks/doc-names-missing-file.ts";
import { assertionlessTest } from "./checks/assertionless-test.ts";
import { specTraceabilityChecks } from "./checks/spec-traceability.ts";
import { DEFAULT_SLICE_BYTE_BOUND, readingPass } from "./reading-pass.ts";
import { loadApiKey } from "./model.ts";

export const CHECKS: readonly Check[] = [
  staleTodo,
  docNamesMissingFile,
  assertionlessTest,
  ...specTraceabilityChecks,
];

export type Outcome = "ok" | "bounded" | "failed";

export interface RunRecord {
  runId: string;
  startedAt: string;
  endedAt: string;
  slice: string | null;
  outcome: Outcome;
  error?: string;
  /** Candidates produced, before dedup against the ledger. */
  candidates: number;
  refuted: number;
  unevaluated: number;
  /** Candidates the reading pass could not support against the real file. */
  discarded: number;
  /** Candidates dropped by the ledger for missing a field a reader needs (R6). */
  malformed: number;
  /**
   * R13's corpus assertion: secret-looking spans found in everything written so far. Any value
   * above zero is a defect in the audit itself, not in the repository it audits.
   */
  corpusLeaks: number;
  admitted: number;
  moved: number;
  redactions: number;
  tokensIn: number;
  tokensOut: number;
  /** null when no price is configured. A fabricated number would be worse than none. */
  costUsd: number | null;
  durationMs: number;
  modelUsed: boolean;
}

export interface RunPaths {
  ledger: string;
  coverage: string;
  runs: string;
}

export function storePaths(store: string, repoName = "nativeweb"): RunPaths {
  return {
    ledger: `${store}/ledger/${repoName}.jsonl`,
    coverage: `${store}/ledger/${repoName}-coverage.json`,
    runs: `${store}/runs`,
  };
}

async function git(args: string[]): Promise<string> {
  const { code, stdout, stderr } = await new Deno.Command("git", {
    args,
    stdout: "piped",
    stderr: "piped",
  }).output();
  if (code !== 0) throw new Error(`git ${args.join(" ")} failed: ${new TextDecoder().decode(stderr)}`);
  return new TextDecoder().decode(stdout);
}

/** R1. A clone that is deleted at the end of the run, wherever the run ends. */
export async function withThrowawayClone<T>(
  source: string,
  fn: (dir: string) => Promise<T>,
): Promise<T> {
  const dir = await Deno.makeTempDir({ prefix: "nw-audit-clone-" });
  try {
    await git(["clone", "--quiet", "--no-hardlinks", "--depth", "50", source, dir]);
    return await fn(dir);
  } finally {
    // Best effort: a clone left behind is a disk-space problem, not a correctness one, and it
    // must never mask the error that brought us here.
    try {
      await Deno.remove(dir, { recursive: true });
    } catch (cause) {
      console.error(`audit: could not remove ${dir}: ${cause}`);
    }
  }
}

export function makeBlame(repoDir: string): CheckContext["blame"] {
  return async (path: string, line: number): Promise<Blame | null> => {
    try {
      const out = await git([
        "-C",
        repoDir,
        "blame",
        "--line-porcelain",
        "-L",
        `${line},${line}`,
        "--",
        path,
      ]);
      const time = /^author-time (\d+)$/m.exec(out);
      const name = /^author (.*)$/m.exec(out);
      if (time === null) return null;
      return {
        authorTime: new Date(Number(time[1]) * 1000),
        authorName: (name?.[1] ?? "unknown").trim(),
      };
    } catch {
      return null; // A file added but never committed has no blame. Not an error.
    }
  };
}

async function readSliceFiles(repoDir: string, slice: string, paths: string[]): Promise<SourceFile[]> {
  const prefix = slice === "." ? "" : `${slice}/`;
  const inSlice = paths.filter((p) => {
    if (!p.startsWith(prefix)) return false;
    return !p.slice(prefix.length).includes("/");
  });
  const files: SourceFile[] = [];
  for (const path of inSlice) {
    try {
      files.push({ path, text: await Deno.readTextFile(`${repoDir}/${path}`) });
    } catch {
      // Tracked but unreadable here: a symlink, or a file the shallow clone did not bring.
    }
  }
  return files;
}

/** Every file the audit has written, scanned for anything that looks like a credential. */
export async function auditCorpusLeaks(paths: RunPaths): Promise<number> {
  const files: Array<{ path: string; text: string }> = [];
  const add = async (path: string) => {
    try {
      files.push({ path, text: await Deno.readTextFile(path) });
    } catch {
      // Not written yet, or not written at all. Nothing to scan.
    }
  };
  await add(paths.ledger);
  await add(paths.coverage);
  try {
    for await (const entry of Deno.readDir(paths.runs)) {
      if (entry.isFile && entry.name.endsWith(".json")) await add(`${paths.runs}/${entry.name}`);
    }
  } catch {
    // No runs directory yet.
  }
  return scanCorpus(files).length;
}

export interface RunOptions {
  /** What to clone. A bare mirror on the audit host; the working repository in a local check. */
  source: string;
  store: string;
  runId: string;
  now: Date;
  useModel: boolean;
  sliceByteBound: number;
  /** Force a slice instead of taking the least recently audited one. For a local check only. */
  forceSlice?: string;
  apiKey?: string | null;
  costPerMTokIn?: number | null;
  costPerMTokOut?: number | null;
}

export async function runOnce(options: RunOptions): Promise<RunRecord> {
  const started = options.now;
  const paths = storePaths(options.store);
  const record: RunRecord = {
    runId: options.runId,
    startedAt: started.toISOString(),
    endedAt: started.toISOString(),
    slice: null,
    outcome: "failed",
    candidates: 0,
    refuted: 0,
    unevaluated: 0,
    discarded: 0,
    malformed: 0,
    corpusLeaks: 0,
    admitted: 0,
    moved: 0,
    redactions: 0,
    tokensIn: 0,
    tokensOut: 0,
    costUsd: null,
    durationMs: 0,
    modelUsed: false,
  };

  try {
    await Deno.mkdir(`${options.store}/ledger`, { recursive: true });
    await Deno.mkdir(paths.runs, { recursive: true });

    await withThrowawayClone(options.source, async (repoDir) => {
      const paths_ = await trackedFiles(repoDir);
      const coverage = mergeCoverage(await readCoverage(paths.coverage), enumerateSlices(paths_));
      const chosen = options.forceSlice !== undefined
        ? coverage.find((r) => r.slice === options.forceSlice) ?? null
        : selectSlice(coverage);

      if (chosen === null) {
        record.outcome = "ok";
        return;
      }
      record.slice = chosen.slice;

      const files = await readSliceFiles(repoDir, chosen.slice, paths_);
      const allPaths = new Set(paths_);
      const ctx: CheckContext = {
        slice: chosen.slice,
        files,
        allPaths,
        now: options.now,
        blame: makeBlame(repoDir),
        readFile: async (p) => {
          try {
            return await Deno.readTextFile(`${repoDir}/${p}`);
          } catch {
            return null;
          }
        },
      };

      const produced: Candidate[] = [];
      for (const check of CHECKS) {
        try {
          produced.push(...await check.run(ctx));
        } catch (cause) {
          // One broken rule must not cost the whole run. Say which, and carry on.
          console.error(`audit: rule ${check.ruleId} threw: ${cause}`);
        }
      }

      let bounded = false;
      if (options.useModel) {
        // `??` would be wrong here: an explicit null means "there is no key", and `null ?? x`
        // still evaluates x. That difference is the difference between a test that stubs the
        // key out and a test that quietly makes a real, billed API call.
        const apiKey = options.apiKey !== undefined ? options.apiKey : await loadApiKey();
        if (apiKey === null) {
          throw new Error(
            "no ZAI_API_KEY in the environment and no readable ~/.zai; the reading pass cannot run",
          );
        }
        const pass = await readingPass(chosen.slice, files, {
          apiKey,
          sliceByteBound: options.sliceByteBound,
        });
        produced.push(...pass.candidates);
        record.refuted = pass.refuted;
        record.unevaluated = pass.unevaluated;
        record.discarded = pass.discarded;
        record.tokensIn = pass.usage.inputTokens;
        record.tokensOut = pass.usage.outputTokens;
        record.modelUsed = true;
        bounded = pass.bounded;
      }

      // R13, and it must precede the ledger: identity is computed over the redacted excerpt.
      const safe: Candidate[] = [];
      for (const candidate of produced) {
        const { candidate: clean, redacted } = redactCandidate(candidate);
        record.redactions += redacted.length;
        safe.push(clean);
      }
      record.candidates = safe.length;

      const current = collapse(await readLedger(paths.ledger));
      const result = await admit(safe, current, options.runId, options.now);
      record.malformed = result.dropped.length;
      record.admitted = result.admitted.length;
      record.moved = result.moved.length;
      for (const drop of result.dropped) {
        console.error(`audit: dropped a ${drop.candidate.ruleId} candidate: ${drop.reason}`);
      }

      await appendLedger(paths.ledger, [...result.admitted, ...result.moved]);
      await writeCoverage(
        paths.coverage,
        recordAudited(coverage, chosen.slice, options.now, result.admitted.length),
      );
      record.outcome = bounded ? "bounded" : "ok";
    });
  } catch (cause) {
    record.outcome = "failed";
    record.error = cause instanceof Error ? `${cause.name}: ${cause.message}` : String(cause);
  }

  // R13's corpus assertion. Not a write-time check: a pattern added in November has to catch
  // what was written in September, so it re-reads everything the audit has ever written. A
  // non-zero count is a defect in the audit, and the digest surfaces it rather than the run
  // failing over it — losing today's findings would not un-write yesterday's leak.
  record.corpusLeaks = await auditCorpusLeaks(paths);
  if (record.corpusLeaks > 0) {
    console.error(
      `audit: R13 VIOLATION — ${record.corpusLeaks} secret-looking span(s) in the written corpus`,
    );
  }

  record.endedAt = new Date().toISOString();
  record.durationMs = Math.max(0, new Date(record.endedAt).getTime() - started.getTime());

  if (options.costPerMTokIn != null && options.costPerMTokOut != null) {
    record.costUsd = (record.tokensIn / 1e6) * options.costPerMTokIn +
      (record.tokensOut / 1e6) * options.costPerMTokOut;
  }

  // R12. Written even when the run failed, and written last, so a crash between the ledger write
  // and here is visible as a missing record rather than as a successful run.
  try {
    await Deno.mkdir(paths.runs, { recursive: true });
    await Deno.writeTextFile(
      `${paths.runs}/${options.runId}.json`,
      JSON.stringify(record, null, 2) + "\n",
    );
  } catch (cause) {
    console.error(`audit: could not write the run record: ${cause}`);
  }
  return record;
}

if (import.meta.main) {
  const args = parseArgs(Deno.args, {
    string: ["source", "store", "run-id", "slice", "bound", "cost-in", "cost-out"],
    boolean: ["no-model", "help"],
    default: { store: `${Deno.env.get("HOME") ?? "."}/audit` },
  });

  if (args.help || args.source === undefined) {
    console.log(`Usage: deno run -A run.ts --source <repo-or-mirror> [options]

  --source <path>    what to clone. On the audit host, ~/audit/mirror/nativeweb.git
  --store <path>     where the ledger, coverage and run records live (default ~/audit)
  --run-id <id>      defaults to today's date
  --slice <path>     audit this slice instead of the least recently audited one
  --bound <bytes>    slice byte bound for the reading pass (default ${DEFAULT_SLICE_BYTE_BOUND})
  --no-model         mechanical rules only; makes no network call and costs nothing
  --cost-in <usd>    price per million input tokens, for the cost field
  --cost-out <usd>   price per million output tokens

This never writes to --source. It clones it, reads the clone, and deletes the clone.`);
    Deno.exit(args.help ? 0 : 2);
  }

  const now = new Date();
  const record = await runOnce({
    source: args.source,
    store: args.store,
    runId: args["run-id"] ?? now.toISOString().slice(0, 10),
    now,
    useModel: !args["no-model"],
    sliceByteBound: Number(args.bound ?? DEFAULT_SLICE_BYTE_BOUND),
    forceSlice: args.slice,
    costPerMTokIn: args["cost-in"] !== undefined ? Number(args["cost-in"]) : null,
    costPerMTokOut: args["cost-out"] !== undefined ? Number(args["cost-out"]) : null,
  });

  console.log(JSON.stringify(record, null, 2));
  Deno.exit(record.outcome === "failed" ? 1 : 0);
}
