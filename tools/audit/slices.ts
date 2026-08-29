// R4 (coverage is recorded) and R5 (least-recently-audited first).
//
// A slice is a directory that directly contains source. Directories, not files, because the
// unit has to be big enough that reading it is worth a model call and small enough that reading
// it fits in one. And because "the audit has never looked at libs/nativeweb.auth" is a sentence
// a person can act on, where "the audit has never looked at 41 of 3,880 files" is not.
//
// The enumeration core is pure: it takes a list of paths. The only function that touches git is
// `trackedFiles`, and it exists because spec 012 lost an afternoon to a grep that matched
// apps/web/bin/.../publish/wwwroot/vendor/htmx/htmx.min.js — stale build output that was still
// on disk. `git ls-files` cannot make that mistake, so the file list comes from git and the
// ignore rules below are a second line rather than the first.

export interface SliceRow {
  slice: string;
  /** ISO-8601, or null when this slice has never been audited. */
  lastAuditedAt: string | null;
  findingsAtLastAudit: number;
}

export interface CoverageSummary {
  total: number;
  never: number;
  audited: number;
  /** The least recently audited slice with a date, for the digest's one-line health claim. */
  stalest: SliceRow | null;
  oldestAuditAt: string | null;
}

/** Extensions that make a directory worth reading. Everything else is payload, not source. */
export const SOURCE_EXTENSIONS: readonly string[] = [
  ".cs", ".csproj", ".props", ".targets", ".slnx",
  ".ts", ".js", ".mjs",
  ".sh", ".ps1", ".py",
  ".css", ".html",
  ".md", ".json", ".yml", ".yaml", ".sql",
];

/**
 * Directories that are never a slice. `git ls-files` already excludes most of these; they are
 * repeated because a caller may pass a plain filesystem walk, and because a tracked file under
 * one of these paths is vendored or generated either way.
 */
export const EXCLUDED_SEGMENTS: readonly string[] = [
  ".git", ".github", ".worktrees", ".claude", ".vs", ".idea",
  "bin", "obj", "node_modules", "dist", "TestResults", "tmp",
  "vendor", "site", "wwwroot",
];

function normalise(path: string): string {
  return path.replaceAll("\\", "/").replace(/^\.\//, "");
}

export function isExcluded(path: string): boolean {
  return normalise(path).split("/").some((seg) => EXCLUDED_SEGMENTS.includes(seg));
}

export function isSource(path: string): boolean {
  const lower = normalise(path).toLowerCase();
  return SOURCE_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

/** The directory a file belongs to. Files at the repository root are the slice `.`. */
export function sliceOf(path: string): string {
  const p = normalise(path);
  const cut = p.lastIndexOf("/");
  return cut === -1 ? "." : p.slice(0, cut);
}

/** Every directory that directly contains at least one source file, sorted, deduplicated. */
export function enumerateSlices(paths: Iterable<string>): string[] {
  const slices = new Set<string>();
  for (const path of paths) {
    if (isExcluded(path) || !isSource(path)) continue;
    slices.add(sliceOf(path));
  }
  return [...slices].sort();
}

/**
 * Fold a fresh enumeration into the stored coverage table.
 *
 * A slice that has appeared since the last run enters as `never`. A slice that has vanished —
 * a directory deleted or renamed — is dropped, because carrying it forever would make the
 * coverage answer wrong in the direction that looks worse than reality.
 */
export function mergeCoverage(existing: readonly SliceRow[], discovered: readonly string[]): SliceRow[] {
  const byName = new Map(existing.map((row) => [row.slice, row]));
  return discovered.map((slice) =>
    byName.get(slice) ?? { slice, lastAuditedAt: null, findingsAtLastAudit: 0 }
  ).sort((a, b) => a.slice.localeCompare(b.slice));
}

/**
 * R5. `never` before any date, then oldest first, then by name so the choice is deterministic
 * and two runs on the same table pick the same slice.
 */
export function orderByStaleness(coverage: readonly SliceRow[]): SliceRow[] {
  return [...coverage].sort((a, b) => {
    if (a.lastAuditedAt === null && b.lastAuditedAt === null) return a.slice.localeCompare(b.slice);
    if (a.lastAuditedAt === null) return -1;
    if (b.lastAuditedAt === null) return 1;
    if (a.lastAuditedAt !== b.lastAuditedAt) return a.lastAuditedAt < b.lastAuditedAt ? -1 : 1;
    return a.slice.localeCompare(b.slice);
  });
}

export function selectSlice(coverage: readonly SliceRow[]): SliceRow | null {
  return orderByStaleness(coverage)[0] ?? null;
}

/** Stamp a slice as audited. Returns a new table; the input is not mutated. */
export function recordAudited(
  coverage: readonly SliceRow[],
  slice: string,
  at: Date,
  findings: number,
): SliceRow[] {
  return coverage.map((row) =>
    row.slice === slice
      ? { slice, lastAuditedAt: at.toISOString(), findingsAtLastAudit: findings }
      : row
  );
}

export function summarise(coverage: readonly SliceRow[]): CoverageSummary {
  const audited = coverage.filter((r) => r.lastAuditedAt !== null);
  const ordered = orderByStaleness(audited);
  return {
    total: coverage.length,
    never: coverage.length - audited.length,
    audited: audited.length,
    stalest: ordered[0] ?? null,
    oldestAuditAt: ordered[0]?.lastAuditedAt ?? null,
  };
}

export async function readCoverage(path: string): Promise<SliceRow[]> {
  try {
    const parsed = JSON.parse(await Deno.readTextFile(path));
    return Array.isArray(parsed) ? parsed as SliceRow[] : [];
  } catch (cause) {
    if (cause instanceof Deno.errors.NotFound) return [];
    throw cause;
  }
}

export async function writeCoverage(path: string, coverage: readonly SliceRow[]): Promise<void> {
  await Deno.writeTextFile(path, JSON.stringify(coverage, null, 2) + "\n");
}

/**
 * Tracked files only. Build output is not audited, and this is the reason: on 2026-08-29 a grep
 * for Alpine directives matched a stale `htmx.min.js` under `apps/web/bin/`, which was still on
 * htmx 2.0.10 after the upgrade. `git ls-files` reports 0 files under `apps/web/bin/`.
 */
export async function trackedFiles(repoRoot: string): Promise<string[]> {
  const command = new Deno.Command("git", {
    args: ["-C", repoRoot, "ls-files", "-z"],
    stdout: "piped",
    stderr: "piped",
  });
  const { code, stdout, stderr } = await command.output();
  if (code !== 0) {
    throw new Error(`git ls-files failed in ${repoRoot}: ${new TextDecoder().decode(stderr)}`);
  }
  return new TextDecoder().decode(stdout).split("\0").filter((p) => p.length > 0);
}
