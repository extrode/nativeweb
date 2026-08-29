import { assert, assertEquals } from "@std/assert";
import {
  enumerateSlices,
  isExcluded,
  isSource,
  mergeCoverage,
  orderByStaleness,
  readCoverage,
  recordAudited,
  selectSlice,
  type SliceRow,
  sliceOf,
  summarise,
  trackedFiles,
  writeCoverage,
} from "./slices.ts";

function row(slice: string, lastAuditedAt: string | null, findings = 0): SliceRow {
  return { slice, lastAuditedAt, findingsAtLastAudit: findings };
}

// --- classification ------------------------------------------------------------------------

Deno.test("build output and vendored trees are never slices", () => {
  for (
    const path of [
      "apps/web/bin/Release/net10.0/publish/wwwroot/vendor/htmx/htmx.min.js",
      "apps/web/obj/Debug/x.cs",
      "libs/nativeweb.assets/../node_modules/y/index.js",
      ".worktrees/feat-x/apps/web/Program.cs",
      "tmp/probe.cs",
      "apps/web/wwwroot/vendor/alpine/alpine.min.js",
      "docs/site/index.html",
    ]
  ) {
    assert(isExcluded(path), `expected excluded: ${path}`);
  }
});

Deno.test("ordinary source paths are not excluded", () => {
  for (const path of ["apps/web/Program.cs", "libs/nativeweb.auth/Tokens.cs", "docs/README.md"]) {
    assert(!isExcluded(path), `expected included: ${path}`);
  }
});

Deno.test("source detection covers the languages in this repo and rejects payload", () => {
  for (const p of ["a.cs", "b.ts", "c.ps1", "d.sh", "e.py", "f.md", "g.css", "h.sql", "i.csproj"]) {
    assert(isSource(p), `expected source: ${p}`);
  }
  for (const p of ["a.png", "b.woff2", "c.pdf", "d.ico", "e.zip"]) {
    assert(!isSource(p), `expected non-source: ${p}`);
  }
});

Deno.test("a file at the repository root belongs to the slice '.'", () => {
  assertEquals(sliceOf("README.md"), ".");
  assertEquals(sliceOf("apps/web/Program.cs"), "apps/web");
  assertEquals(sliceOf("apps\\web\\Program.cs"), "apps/web");
});

// --- enumeration (R4) ----------------------------------------------------------------------

Deno.test("enumeration returns each source directory once, sorted", () => {
  const slices = enumerateSlices([
    "apps/web/Program.cs",
    "apps/web/Layout.cs",
    "libs/nativeweb.auth/Tokens.cs",
    "README.md",
    "apps/web/bin/Debug/Stale.cs",
    "apps/web/logo.png",
  ]);
  assertEquals(slices, [".", "apps/web", "libs/nativeweb.auth"]);
});

Deno.test("a directory holding only non-source files is not a slice", () => {
  assertEquals(enumerateSlices(["assets/img/logo.png", "assets/img/hero.webp"]), []);
});

// --- coverage merging ----------------------------------------------------------------------

Deno.test("a newly appeared directory enters coverage as never audited", () => {
  const merged = mergeCoverage([row("apps/web", "2026-08-01T00:00:00.000Z", 3)], [
    "apps/web",
    "libs/nativeweb.signin.epass",
  ]);
  assertEquals(merged.length, 2);
  assertEquals(merged[0].slice, "apps/web");
  assertEquals(merged[0].lastAuditedAt, "2026-08-01T00:00:00.000Z");
  assertEquals(merged[1].lastAuditedAt, null);
});

Deno.test("a directory that no longer exists is dropped from coverage", () => {
  const merged = mergeCoverage(
    [row("apps/web", "2026-08-01T00:00:00.000Z"), row("apps/gone", "2026-08-02T00:00:00.000Z")],
    ["apps/web"],
  );
  assertEquals(merged.map((r) => r.slice), ["apps/web"]);
});

// --- selection (R5) ------------------------------------------------------------------------

Deno.test("a never-audited slice is selected ahead of one audited yesterday", () => {
  const selected = selectSlice([
    row("apps/web", "2026-08-28T00:00:00.000Z"),
    row("libs/nativeweb.auth", null),
  ]);
  assertEquals(selected?.slice, "libs/nativeweb.auth");
});

Deno.test("with no never-audited slice, the oldest audit wins", () => {
  const selected = selectSlice([
    row("apps/web", "2026-08-28T00:00:00.000Z"),
    row("libs/a", "2026-01-04T00:00:00.000Z"),
    row("libs/b", "2026-06-11T00:00:00.000Z"),
  ]);
  assertEquals(selected?.slice, "libs/a");
});

Deno.test("selection is deterministic when staleness ties", () => {
  const tied = [row("libs/b", null), row("libs/a", null), row("apps/web", null)];
  assertEquals(selectSlice(tied)?.slice, "apps/web");
  assertEquals(orderByStaleness(tied).map((r) => r.slice), ["apps/web", "libs/a", "libs/b"]);
  // Same table, shuffled: same answer.
  assertEquals(selectSlice([...tied].reverse())?.slice, "apps/web");
});

Deno.test("selecting from an empty table is null, not a throw", () => {
  assertEquals(selectSlice([]), null);
});

Deno.test("the run after an audit picks a different slice", () => {
  let coverage = [row("apps/web", null), row("libs/auth", null)];
  const first = selectSlice(coverage)!;
  coverage = recordAudited(coverage, first.slice, new Date("2026-08-30T03:00:00.000Z"), 2);
  const second = selectSlice(coverage)!;
  assert(second.slice !== first.slice, "the same slice was picked twice in a row");
  assertEquals(coverage.find((r) => r.slice === first.slice)?.findingsAtLastAudit, 2);
});

Deno.test("recordAudited does not mutate the table it is given", () => {
  const before = [row("apps/web", null)];
  recordAudited(before, "apps/web", new Date("2026-08-30T03:00:00.000Z"), 5);
  assertEquals(before[0].lastAuditedAt, null);
});

// --- summary (R4) --------------------------------------------------------------------------

Deno.test("the summary counts never-audited slices and names the stalest audited one", () => {
  const summary = summarise([
    row("apps/web", "2026-08-28T00:00:00.000Z"),
    row("libs/a", "2026-01-04T00:00:00.000Z"),
    row("libs/b", null),
    row("libs/c", null),
  ]);
  assertEquals(summary.total, 4);
  assertEquals(summary.never, 2);
  assertEquals(summary.audited, 2);
  assertEquals(summary.stalest?.slice, "libs/a");
  assertEquals(summary.oldestAuditAt, "2026-01-04T00:00:00.000Z");
});

Deno.test("a table where nothing has been audited has no stalest slice", () => {
  const summary = summarise([row("a", null)]);
  assertEquals(summary.audited, 0);
  assertEquals(summary.stalest, null);
});

// --- persistence ---------------------------------------------------------------------------

Deno.test("coverage round-trips through disk, and a missing file reads as empty", async () => {
  const dir = await Deno.makeTempDir({ prefix: "nw-audit-cov-" });
  const path = `${dir}/coverage.json`;
  try {
    assertEquals(await readCoverage(path), []);
    const coverage = [row("apps/web", "2026-08-30T03:00:00.000Z", 1), row("libs/a", null)];
    await writeCoverage(path, coverage);
    assertEquals(await readCoverage(path), coverage);
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

// --- the one function that touches git -------------------------------------------------------

Deno.test({
  name: "trackedFiles reports no build output for this repository",
  permissions: { read: true, run: ["git"], env: true },
  async fn() {
    const root = new URL("../..", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
    const files = await trackedFiles(root);
    assert(files.length > 100, `expected a populated repository, got ${files.length} files`);
    assertEquals(files.filter((f) => f.startsWith("apps/web/bin/")).length, 0);
    assertEquals(files.filter((f) => f.includes("/obj/")).length, 0);
    // A file that is tracked whatever branch this runs on, so the assertion does not depend
    // on whether the module under test has been committed yet.
    assert(files.includes("AGENTS.md"), "AGENTS.md should be tracked");
    assert(
      files.some((f) => f.startsWith("apps/web/") && f.endsWith(".cs")),
      "expected tracked C# under apps/web",
    );
  },
});
