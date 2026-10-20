# 017 — Plan
Spec: ./017-spec.md

## Approach

The seam gains two parameters and loses one. `IScaffoldStore<T>.ListAsync` today takes
`(search, searchFields, skip, take, ct)`; it becomes `(search, searchFields, sort, sortFields,
after, take, ct)`, where `sort` is a validated property name plus a direction, `sortFields` is the
`[ScaffoldSort]` allow-list the generator passes down exactly as it already passes `searchFields`,
and `after` is a decoded cursor or null. `CountAsync` is untouched, because the list still reports
a total (R11) and every existing implementation of it keeps working. The offset parameter goes.

**The division of labour is that the framework owns the cursor's opacity and the store owns its
types.** A cursor is an envelope of four strings — sort property, direction, the sorted column's
value, the key's value — base64url-encoded by `ScaffoldCursor` so a client is not invited to read
or construct one (R8). The framework never interprets the two value strings; it cannot, because
only the store knows whether a column is a `TIMESTAMPTZ` or an `INTEGER`. The store parses them,
and a parse failure is a malformed cursor, which by R9 renders the first page rather than an
error. The alternative — a typed, framework-side cursor — would need the framework to know every
model's property types at runtime, which is either reflection (not NativeAOT-clean, R15) or a
second generated surface for no gain.

**Sorting reaches PostgreSQL as a choice between static queries, not as a composed string.**
JauntyQ is fixed SQL generated from `.sql` files at build time; there is no query builder, and
`AGENTS.md` binds all data access to it. So a keyset page over column C ascending is its own file
ending `WHERE (C, id) > (@afterC, @afterId) ORDER BY C, id LIMIT @take`, and its mirror ends
`WHERE (C, id) < (@afterC, @afterId) ORDER BY C DESC, id DESC LIMIT @take`. Because the row-value
comparison `(a,b) < (x,y)` decomposes to `a<x OR (a=x AND b<y)`, the mirror serves both descending
next-page and ascending previous-page — the latter by reversing the returned rows in memory. Two
files per sortable column. The validated property name selects *which file runs*; nothing is ever
interpolated, so injection is structurally impossible and PostgreSQL can serve both directions
from one `(C, id)` index.

Two alternatives were evaluated and rejected. A single query with `ORDER BY CASE WHEN @sort=1 THEN
title END, …` fails twice over: each `CASE` is its own sort key so `TEXT`, `TIMESTAMPTZ` and
`INTEGER` never unify into one comparable expression, no index survives a computed sort key so
every page becomes a seq-scan-and-sort, and the keyset `WHERE` cannot be written at all because one
`@after` parameter would have to be four types at once. Keeping offset paging in the sample and
exposing keyset only in the contract was rejected as dishonest: `JauntyArticleStore` exists to
prove the seam is satisfiable with JauntyQ, and a keyset signature over an `OFFSET` body means the
new semantics are exercised by no implementation anywhere, leaving every consumer to invent the
mirrored predicate and the NULLS handling alone.

## Tech / dependencies

No new dependencies. .NET 10 / C# 14, NativeAOT, `TreatWarningsAsErrors` repo-wide. Roslyn
incremental source generator (the existing `ScaffoldGenerator`, `ForAttributeWithMetadataName`).
JauntyQ from the local checkout, PostgreSQL. Cursor encoding uses
`System.Buffers.Text.Base64Url` — no package, no hand-rolled base64. Verified against this
repository's SDK on 2026-09-01 rather than assumed: `EncodeToString`, `IsValid` and
`TryDecodeFromChars` all resolve on `net10.0`, and `IsValid` returns false for non-base64url input,
which is what R9's rejection path rests on.
Tests: xUnit, `WebApplicationFactory` against real PostgreSQL, `Microsoft.Playwright`.

## Changes by area

- **`libs/nativeweb.scaffold/ScaffoldAttributes.cs`** — add `ScaffoldSortAttribute`, a property
  marker with no members, sitting beside `ScaffoldSearchAttribute` and documented the same way.
  **R1.**

- **`libs/nativeweb.scaffold/ScaffoldSort.cs`** *(new)* — `ScaffoldSortDirection` (Ascending,
  Descending); `ScaffoldSortSpec`, a readonly record struct of validated property name plus
  direction; `ScaffoldCursor`, a readonly record struct of (sort property, direction, sort value,
  key value) with `Encode()` and `TryDecode(string?, out ScaffoldCursor)`. `TryDecode` returns
  false — never throws — for null, empty, non-base64url, wrong field count, or oversized input,
  and the caller treats false as "start from the beginning". **R3, R4, R7, R8, R9.**

- **`libs/nativeweb.scaffold/IScaffoldStore.cs`** — `ListAsync` signature changes to
  `(string? search, IReadOnlyList<string> searchFields, ScaffoldSortSpec? sort,
  IReadOnlyList<string> sortFields, ScaffoldCursor? after, int take, CancellationToken ct)`,
  returning `IReadOnlyList<T>`. The XML doc gains the same reasoning `searchFields` carries about
  why the allow-list is a parameter rather than something the store re-declares, plus an explicit
  contract clause: **the store must apply the key as a final tiebreaker on every sort**, and a
  nullable sorted column must fix its NULL position rather than leaving it to the engine.
  `CountAsync` unchanged. **R4, R6, R7, R11.**

- **`libs/nativeweb.scaffold.generator/ScaffoldModel.cs`** — `PropertyModel.Sortable`, added to
  `Equals` and `GetHashCode` so the incremental pipeline does not cache across a change to it.
  Omitting it there is the defect class that makes a generator return stale output. **R1.**

- **`libs/nativeweb.scaffold.generator/ScaffoldGenerator.cs`** — one `case
  "NativeWeb.Scaffold.ScaffoldSortAttribute"` in the existing per-property attribute switch. The
  key property is treated as sortable whether or not it is marked, since it is the default order.
  **R1, R3.**

- **`libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs`** — the largest change, all inside
  `EmitList` and the shared `ScaffoldLayout`:
  - emit a `SortFields` array beside the existing `SearchFields`;
  - read `?sort=` and `?dir=` from the query string and **reject a name not in `SortFields`**,
    falling back to the key ascending, so an unknown name never reaches the store (**R2, R3, R5**);
  - read `?after=` through `ScaffoldCursor.TryDecode`, and discard a cursor whose sort property or
    direction does not match the request's, which is the "does not match the current sort" half of
    **R9**;
  - call `ListAsync` with `take: PageSize + 1` and trim, so "is there a next page" is answered
    without a second query (**R10**);
  - render `<th>` for a `[ScaffoldSort]` column as a link that toggles direction, carrying
    `aria-sort` and a marker class for the active column (**R13**);
  - replace `ScaffoldLayout.Pager` with a cursor pager: Newer / total / Older, where the two links
    carry the first and last row's cursor and are omitted at the ends of the order (**R10, R11**);
  - every emitted link preserves `q`, `sort` and `dir` together (**R12**);
  - delete the `MaxPage` clamp and its comment, which existed only to stop an overflowed offset.
  - The `PageSize = 20` constant stays.

- **`apps/sample/DemoStore.cs`** — `MemoryStore<T>` gains `protected abstract IComparable?
  SortValue(T item, string name)` mirroring the existing `Field`, and a `protected abstract string
  SortText(T item, string name)` for the cursor's round-trippable form. `ListAsync` orders by the
  requested property with the key appended as tiebreaker, then skips forward past the cursor by
  comparison rather than by index. `ArticleStore` and `TagStore` implement both with the same
  `switch` shape they already use. No reflection, so **R15** holds. **R2, R3, R4, R7, R9.**

- **`apps/sample/JauntyArticleStore.cs`** — `ListAsync` becomes a `switch` over
  `(sort.Property, sort.Direction)` selecting one generated JauntyQ method, with the key-ordered
  pair as the default and as the fallback for a property outside the allow-list. Previous-page
  reverses the mirrored query's rows in memory. Cursor values are parsed per column
  (`DateTimeOffset.TryParse` invariant / `int.TryParse` invariant / the string as-is), and a parse
  failure returns the first page. **R2, R4, R7, R9.**

- **`db/sample/queries/Articles/`** *(8 new files)* — `ListByKeyAsc.sql`, `ListByKeyDesc.sql`,
  `ListByTitleAsc.sql`, `ListByTitleDesc.sql`, `ListByAuthorAsc.sql`, `ListByAuthorDesc.sql`,
  `ListByCreatedAsc.sql`, `ListByCreatedDesc.sql`. Each keeps the existing three-column ILIKE
  predicate and its `@pattern` convention, adds the row-value keyset comparison, and ends
  `ORDER BY <col>, id LIMIT @take` or its mirror. **`author` is the nullable one and gets explicit
  `NULLS LAST` on ascending and `NULLS FIRST` on descending, with the keyset predicate widened to
  carry NULL rows across the page boundary** — without it every page after the first silently drops
  author-less rows. `List.sql` is superseded and removed; `Count.sql` is untouched. **R2, R4.**

- **`db/sample/migrations/0002_scaffold_sort_indexes.sql`** *(new)* — `(title, id)`,
  `(author NULLS LAST, id)` and `(created_at, id)` indexes, so the reference app's keyset paging is
  index-served rather than sorted per page. Carries a note in the register `0001` already uses:
  the ILIKE search predicate is still not index-servable, so a filtered list still scans, and the
  index earns its place on the unfiltered list.

- **`apps/sample/PublishedArticles.cs`** — third `IScaffoldStore<T>` implementation in the tree;
  signature and keyset behaviour updated to match. **R6.**

- **`docs/breaking-changes.md`** *(new)* — the document backlog gap **G2** asks for. Entries newest
  first under `## Unreleased`, each naming the seam, what changed, and the before/after a consumer
  migrates by. Seeded with three entries, not one: this `ListAsync` change, and retrospectively the
  removal of `nativeweb.signin.epass` and `nativeweb.auth` on 2026-08-31, which broke justsmtp.com
  with no record anywhere. Linked from the repository `README.md`. **R14.**

- **`libs/nativeweb.scaffold/README.md`** — the attribute table gains `[ScaffoldSort]`; the list
  section describes cursor paging and the store's tiebreaker and NULLS obligations.

- **Tests** — **R16**:
  - `tests/nativeweb.scaffold.tests.unit` — `ScaffoldCursor` round-trip and every `TryDecode`
    rejection path; the emitter's allow-list rejection, default order, direction toggling and link
    preservation, asserted against generated output; `MemoryStore` keyset ordering including the
    duplicate-value and nullable-column cases.
  - `tests/nativeweb.scaffold.tests.integration` — against real PostgreSQL through
    `WebApplicationFactory`: every sortable column both directions; paging the whole table with
    duplicate sort values and asserting each row appears exactly once (**R4**); a row inserted
    mid-page-walk (**R7**); an unknown `?sort=` (**R5**); a hand-mangled `?after=` (**R9**); the
    author-NULL page boundary.
  - `tests/nativeweb.scaffold.tests.e2e` — Playwright: click a column header, assert the order and
    `aria-sort`; walk Older then Newer and land on the same rows; assert a `[ScaffoldList]` column
    without `[ScaffoldSort]` offers no link (**R13**); assert a shared URL reproduces search + sort
    + position (**R12**).
  - Every new test mutation-checked per the repo's testing rule.

## Data / interfaces

```csharp
public enum ScaffoldSortDirection { Ascending, Descending }

public readonly record struct ScaffoldSortSpec(string Property, ScaffoldSortDirection Direction);

public readonly record struct ScaffoldCursor(
    string Property, ScaffoldSortDirection Direction, string SortValue, string Key)
{
    public string Encode();
    public static bool TryDecode(string? value, out ScaffoldCursor cursor);
}

ValueTask<IReadOnlyList<T>> ListAsync(
    string? search,
    IReadOnlyList<string> searchFields,
    ScaffoldSortSpec? sort,
    IReadOnlyList<string> sortFields,
    ScaffoldCursor? after,
    int take,
    CancellationToken ct);
```

Query string contract for a generated list: `?q=` search (unchanged), `?sort=` a declared property
name, `?dir=asc|desc`, `?after=` an opaque cursor. `?page=` is gone and an old link carrying it is
ignored rather than rejected.

SQL shape, ascending over a non-nullable column:

```sql
-- @params pattern:string, afterTitle:string, afterId:string, take:int
SELECT id, title, author, body, published, view_count, created_at
FROM articles
WHERE (title ILIKE @pattern OR COALESCE(author, '') ILIKE @pattern OR body ILIKE @pattern)
  AND (title, id) > (@afterTitle, @afterId)
ORDER BY title, id
LIMIT @take;
```

## Risks & mitigations

- **A nullable sorted column silently loses rows.** `(author, id) > (@a, @id)` is NULL, not true,
  for an author-less row, so every page after the first drops them. Mitigated by explicit
  `NULLS LAST`/`NULLS FIRST` plus a widened predicate in the two `author` files, by an integration
  fact that pages the whole table and counts, and by an XML-doc contract clause so a consumer's
  own store is told. This is the failure most likely to reach a consumer, because it is invisible
  in a small table.
- **The cursor is attacker-supplied input.** It is not a security boundary — it can only reposition
  a client within rows it may already list — but it must not crash or reach the database untyped.
  Mitigated by `TryDecode` never throwing, a length cap before decoding, per-column typed parsing
  in the store with failure meaning "first page", and the values travelling as SQL *parameters*.
  The sort property is separately allow-listed, so no client-supplied string ever selects a
  column.
- **Removing offset paging breaks jump-to-page and any bookmarked `?page=N`.** Accepted and
  decided; mitigated by ignoring `?page=` rather than erroring, and by the breaking-change entry.
- **Sortable columns cost SQL files, two per column.** Accepted as the price of fixed SQL; the
  repo already pays it for `[ScaffoldSearch]`, where adding a searchable column means hand-editing
  the queries. Recorded in the scaffold README so nobody discovers it at the third column.
- **The source generator intermittently drops its whole output on incremental rebuild** — a known
  open item in `work/todo.md` against JauntyQ. If it fires during this work it is that bug, not
  this change; a clean rebuild distinguishes them.
- **AOT.** No reflection anywhere: sort values come from a generated or hand-written `switch`, not
  from `PropertyInfo`. Verified by the existing AOT publish gate, not by inspection.

## Coverage check

| R# | Covered by |
|---|---|
| R1 | `ScaffoldSortAttribute`; `PropertyModel.Sortable`; generator switch case |
| R2 | Emitter `?sort=`/`?dir=` handling; per-column SQL pair; both sample stores |
| R3 | Key ascending as the default order, in emitter and in every store |
| R4 | Key appended as tiebreaker (contract clause); NULLS handling; the page-the-whole-table fact |
| R5 | Emitter validates against `SortFields` before the store is called |
| R6 | `ListAsync` takes `sort` and `sortFields`; no store re-declares them |
| R7 | `ScaffoldCursor`; keyset predicates; the insert-mid-walk fact |
| R8 | Base64url envelope, no documented layout, no client-side construction path |
| R9 | `TryDecode` returns false rather than throwing; mismatched sort discarded; store parse failure means first page |
| R10 | `take: PageSize + 1`; Newer/Older links omitted at the ends |
| R11 | `CountAsync` retained and rendered in the pager |
| R12 | Every emitted link carries `q`, `sort`, `dir` together; e2e shared-URL fact |
| R13 | Sortable-only `<th>` links, `aria-sort`, active-column marker |
| R14 | `docs/breaking-changes.md`, seeded with this change and the two 2026-08-31 removals |
| R15 | No reflection; existing AOT publish gate; warnings-as-errors |
| R16 | Three tiers enumerated under Tests, each mutation-checked |

No requirement is uncovered.
