# JauntyQ feature request: multi-statement result sets (and window-function polish)

Prepared 2026-08-02 against JauntyQ `1a5296f`, from the manzil.my perf-tail work
(`docs/plans/2026-08-02-002-perf-tail-work.md`). Probed empirically: a scratch parser harness
plus a scratch copy of `libs/manzil.data` built through the real generator.

## Motivation: round trips, not query cost

manzil's public list page (`/u/{username}`) runs 3–4 sequential queries per request — count,
page, tag chips (plus the profile lookup). Each query costs ~0.3ms; the round trips dominate.
Parallelizing them across pooled connections was measured and made things *worse* (32 VUs × 3
connections saturated the pool: p95 20.6→57.4ms; even solo, connection acquisition cost more
than the query it overlapped). The correct fix for round-trip latency is **batching: several
statements, one connection, one round trip** — which is a data-layer feature, hence this
request.

## Finding 1 — window functions already work (undocumented?)

`COUNT(*) OVER()` as a projection column generates correctly today:

```sql
-- @params limit:int, offset:int, userId:long
-- @type total bigint
SELECT id, url, title, created_at, COUNT(*) OVER() AS total
FROM bookmarks
WHERE bookmarks.user_id = @userId
  AND bookmarks.is_public
ORDER BY bookmarks.created_at DESC, bookmarks.id DESC
LIMIT @limit OFFSET @offset;
```

Builds clean (0 warnings) and emits a correct mapper: the expression is an opaque projection
item, the SQL passes through verbatim, and the window function evaluates before `LIMIT`, so
`total` is the full filtered count on every returned row. Three rough edges worth polishing:

1. **Type inference doesn't reach window shapes.** Bare `count(...)` infers `bigint`;
   `COUNT(*) OVER()` infers nothing, so the `-- @type` directive is mandatory. The parser
   already special-cases COUNT — extending that to `COUNT(...) OVER(...)` would drop the
   directive requirement for the common case.
2. **`-- @type` cannot express NOT NULL.** `ParseTypeDirective` takes only `<alias> <dbtype>`,
   and `InferredNotNull` stays false for directive-typed expressions, so the property comes out
   `long?` even though `COUNT(*) OVER()` can never be NULL on a returned row. A
   `-- @type total bigint not null` form (or inference, per point 1) would fix the ergonomics.
3. **The directive can't see into CTEs.** Moving the window expression into a CTE and
   projecting it in the outer SELECT as a plain column fails with JNT3006 ("no expression
   projection item uses 'AS total'") — the directive resolver only scans the outer projection
   list. Not blocking (write the window in the outer SELECT), but the error message sends the
   author hunting a spelling mistake that isn't there.

## Finding 2 — multi-statement files silently mis-parse (correctness bug today)

Two semicolon-separated statements in one `.sql` file produce **no diagnostic**. The parser
merges both into a single `QueryModel`:

```
=== two-statements ===
  StatementType: Select
  Unsupported:   []
  Tables:        [bookmarks, bookmarks]
  Col: alias='total' expr=True exprSql='COUNT ( * )' inferred='bigint'
  Col: name='id' ...
  Col: name='title' ...
```

Both statements' tables and projections land in one model, and whatever the generator emits
from that is wrong. Independent of any new feature, **a second top-level statement should be a
hard JNT error** the way `UNION` and free-standing subqueries already are
(`SqlParser.Part6.cs` `DetectUnsupportedConstructs`).

## The request: multiple result sets per file

One `.sql` file, N statements, one generated method, one round trip. Sketch:

```sql
-- @params limit:int, offset:int, userId:long

-- @result Total
SELECT COUNT(*) AS total
FROM bookmarks
WHERE bookmarks.user_id = @userId AND bookmarks.is_public;

-- @result Page
SELECT id, url, title, description, favicon_content_type, created_at
FROM bookmarks
WHERE bookmarks.user_id = @userId AND bookmarks.is_public
ORDER BY bookmarks.created_at DESC, bookmarks.id DESC
LIMIT @limit OFFSET @offset;
```

generating something like:

```csharp
public class Result
{
    public class ListPageBatch
    {
        public required List<Total> Total { get; set; }
        public required List<Page>  Page  { get; set; }
    }
}
// executed as one DbBatch / one command; mapped via reader.NextResult()
```

Design points, from the consumer's chair:

- **Execution**: `DbBatch` (standard ADO.NET since .NET 6) is the natural carrier — one
  `NpgsqlBatchCommand` per statement keeps per-statement parameters and lets Npgsql's
  auto-prepare see each statement individually. Fallback for providers without batch support:
  one command, statements concatenated, `NextResult()` — Npgsql handles both. Provider support
  for `DbBatch` varies (Npgsql and SqlClient yes; others need checking per provider), which
  argues for the generator choosing per provider rather than exposing the choice.
- **Parameters**: one shared `@params` set for the file; each statement uses the subset it
  names. This matches how the count/page pair naturally shares its predicate parameters.
- **Shape guard**: the existing `JauntyQShapeGuard.Validate` runs once per result set, against
  that statement's projection.
- **Semantics to document**: statements execute in order on one connection; whether they share
  an implicit transaction (Postgres simple-query batches do; explicit `DbBatch` commands do
  not) should be stated, not discovered.
- **Naming**: `-- @result <Name>` per statement (error if missing or duplicated), so the
  composite property names are author-chosen, not positional.

## Why not settle for the window function alone?

`COUNT(*) OVER()` covers exactly one pairing (count + page) and forces them into one statement,
which reverses a deliberate manzil design: counts repeat their list predicate verbatim so
visibility filters can't drift between "how many" and "which ones". It also has an edge — an
out-of-range page returns zero rows and therefore *no count at all*, so page-clamping still
needs a fallback query. Multi-statement batching keeps the statements separate and auditable,
covers the third query (tag chips) too, and generalizes to every "N small queries per request"
page in any JauntyQ consumer. The window function remains a fine single-statement tool; the
batch is the general fix.

## Trade-offs acknowledged (none look fatal)

- Generator complexity: statement splitting, N projections, N result types, composite emitter.
  All mechanical extensions of existing machinery; the parser already splits on statement
  boundaries badly (Finding 2), so splitting correctly is work item zero either way.
- Cross-provider batch semantics differ (implicit transactions, prepare behavior); needs a
  short per-provider matrix before the API is fixed.
- Analyzers (N+1, perf hints) need to run per statement — arguably they get *better*: the
  count+page pair in one file is exactly the context the N+1 analyzer wants.

## Reproduction

Parser harness: tokenize + `SqlParser.Parse` on the four cases above (window, window+directive,
plain control, two statements) and dump `QueryModel`. Generator probe: copy of
`manzil.my/libs/manzil.data` + `db/schema` with the window query added, built with
`-p:EmitCompilerGeneratedFiles=true`; emitted `Public.ListPageWithTotal.g.cs` inspected. Both
scratch trees live under `%LOCALAPPDATA%\Temp\tmp\jauntyq-{probe,genprobe}` until cleaned.
