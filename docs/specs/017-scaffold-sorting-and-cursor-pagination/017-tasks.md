# 017 — Tasks
Plan: ./017-plan.md

Build order is dependency order: the contract types first, then the seam, then the generator, then
the three stores, then SQL, then tests, then the documents. Nothing after T3 compiles until T3
lands, which is deliberate — the breaking change is made once and every implementation is brought
to it rather than shimmed.

- [x] T1 The sort attribute — files: `libs/nativeweb.scaffold/ScaffoldAttributes.cs` — covers: R1 — done when: `ScaffoldSortAttribute` exists beside `ScaffoldSearchAttribute` with the same `AttributeUsage`, documented with why the allow-list is declarative, and the library builds.

- [x] T2 Cursor and sort spec — files: `libs/nativeweb.scaffold/ScaffoldSort.cs` (new) — covers: R3, R4, R7, R8, R9 — done when: `ScaffoldSortDirection`, `ScaffoldSortSpec` and `ScaffoldCursor` exist; `Encode`/`TryDecode` round-trip; `TryDecode` returns false without throwing for null, empty, non-base64url, wrong field count and over-length input; unit facts cover each rejection path and each is mutation-checked.

- [x] T3 The seam breaks — files: `libs/nativeweb.scaffold/IScaffoldStore.cs` — covers: R6, R7, R11 — done when: `ListAsync` takes `(search, searchFields, sort, sortFields, after, take, ct)`, `CountAsync` is unchanged, and the XML doc states the two store obligations the framework cannot enforce: the key is the final tiebreaker on every sort, and a nullable sorted column fixes its NULL position.

- [x] T4 Generator reads the attribute — files: `libs/nativeweb.scaffold.generator/ScaffoldModel.cs`, `libs/nativeweb.scaffold.generator/ScaffoldGenerator.cs` — covers: R1, R3 — done when: `PropertyModel.Sortable` is set from `[ScaffoldSort]`, is present in both `Equals` and `GetHashCode` so the incremental cache cannot go stale on it, and the key property is sortable whether or not it is marked.

- [x] T5 Emitter: sort input and validation — files: `libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs` — covers: R2, R3, R5, R9 — done when: the generated list emits `SortFields`, reads `?sort=`/`?dir=`, falls back to the key ascending for a name outside `SortFields`, decodes `?after=` through `TryDecode`, discards a cursor whose property or direction disagrees with the request, and passes a validated spec to the store. The `MaxPage` offset clamp and its comment are gone.

- [x] T6 Emitter: sortable headers and the cursor pager — files: `libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs` — covers: R10, R11, R12, R13 — done when: a `[ScaffoldSort]` column's `<th>` is a direction-toggling link carrying `aria-sort` and an active marker while an unsorted column stays plain text; `ListAsync` is called with `PageSize + 1` and trimmed so next-page existence needs no second query; `Pager` renders Newer / total / Older with the end links omitted; every emitted link carries `q`, `sort` and `dir` together.

- [x] T7 The in-memory store — files: `apps/sample/DemoStore.cs` — covers: R2, R3, R4, R7, R9 — done when: `MemoryStore<T>` sorts by the requested property with the key as tiebreaker and advances past the cursor by comparison rather than by index; `ArticleStore` and `TagStore` supply sort values through a `switch` with no reflection; a malformed or unparseable cursor yields the first page.

- [x] T8 Keyset SQL for the reference app — files: `db/sample/queries/Articles/ListByKeyAsc.sql`, `ListByKeyDesc.sql`, `ListByTitleAsc.sql`, `ListByTitleDesc.sql`, `ListByAuthorAsc.sql`, `ListByAuthorDesc.sql`, `ListByCreatedAsc.sql`, `ListByCreatedDesc.sql`; `db/sample/queries/Articles/List.sql` removed — covers: R2, R4 — done when: each file carries the existing `@pattern` ILIKE predicate plus the row-value keyset comparison and its `ORDER BY <col>, id` or mirror; the two `author` files carry explicit `NULLS LAST`/`NULLS FIRST` and a predicate widened so author-less rows cross the page boundary; JauntyQ generates a method per file with no JNT diagnostics.

- [x] T9 Sort indexes — files: `db/sample/migrations/0002_scaffold_sort_indexes.sql` (new) — covers: R2 — done when: `(title, id)`, `(author NULLS LAST, id)` and `(created_at, id)` exist, the migration applies cleanly on a fresh database, and it records in `0001`'s own register that a filtered list still scans because the ILIKE predicate is not index-servable.

- [x] T10 The JauntyQ store — files: `apps/sample/JauntyArticleStore.cs` — covers: R2, R4, R7, R9 — done when: `ListAsync` switches over `(property, direction)` to one generated method, defaults to the key pair for anything outside the allow-list, reverses the mirrored query's rows for a previous page, parses cursor values per column type, and returns the first page on a parse failure.

- [x] T11 The third implementation — files: `apps/sample/PublishedArticles.cs` — covers: R6 — done when: it satisfies the new signature with the same keyset semantics, and `apps/sample` builds with warnings as errors.

- [x] T12 Unit tier — files: `tests/nativeweb.scaffold.tests.unit/*` — covers: R1, R3, R5, R9, R13, R16 — done when: cursor round-trip and rejection, emitter allow-list rejection, default order, direction toggling, link preservation and `MemoryStore` keyset ordering (duplicate values, nullable column) are each asserted; every new fact is mutation-checked and no test asserts over a file's text where it can assert over behaviour.

- [x] T13 Integration tier — files: `tests/nativeweb.scaffold.tests.integration/*` — covers: R2, R4, R5, R7, R9, R11, R16 — done when: against real PostgreSQL, every sortable column pages both directions; a full walk over duplicate sort values returns each row exactly once; a row inserted mid-walk neither duplicates nor hides a row; an unknown `?sort=` and a mangled `?after=` both render; the author-NULL page boundary carries every row; the reported total matches.

- [ ] T14 End-to-end tier — files: `tests/nativeweb.scaffold.tests.e2e/*` — covers: R10, R12, R13, R16 — done when: Playwright clicks a header and asserts order and `aria-sort`; Older then Newer returns to the same rows; a listed-but-unsortable column offers no link; a copied URL reproduces search, sort and position in a fresh context.

- [ ] T15 The breaking-change log — files: `docs/breaking-changes.md` (new), `README.md` — covers: R14 — done when: the document exists with three entries newest-first — this `ListAsync` change, and retrospectively the 2026-08-31 removals of `nativeweb.signin.epass` and `nativeweb.auth` — each naming the seam, what changed and the before/after a consumer migrates by, and the repository README links it. This closes backlog gap G2.

- [ ] T16 Scaffold documentation — files: `libs/nativeweb.scaffold/README.md` — covers: R1, R13 — done when: the attribute table lists `[ScaffoldSort]`, the list section describes cursor paging, and the store's tiebreaker and NULLS obligations are stated where an implementer reads them rather than only in XML docs.

- [ ] T17 AOT and visibility — files: `CHANGELOG.md`, `work/todo.md`, `work/status/current.md`, `work/tasklist.md` — covers: R15 — done when: the AOT publish gate passes with no new trim or AOT warnings, G5 and G2 are struck from `work/todo.md` with what closed them, the change is under `## [Unreleased]`, and `structure-audit.js` exits 0.

## Analyze

Every requirement maps to at least one task, and every task traces to a "Changes by area" entry.

| R# | Tasks |
|---|---|
| R1 | T1, T4, T12, T16 |
| R2 | T5, T8, T9, T10, T13 |
| R3 | T2, T4, T5, T7, T12 |
| R4 | T2, T3, T7, T8, T10, T13 |
| R5 | T5, T12, T13 |
| R6 | T3, T11 |
| R7 | T2, T3, T7, T10, T13 |
| R8 | T2 |
| R9 | T2, T5, T7, T10, T12, T13 |
| R10 | T6, T14 |
| R11 | T3, T6, T13 |
| R12 | T6, T14 |
| R13 | T6, T12, T14, T16 |
| R14 | T15 |
| R15 | T17 |
| R16 | T12, T13, T14 |

No orphan tasks. No uncovered requirement.

One ordering note the build must respect: T3 breaks compilation for everything downstream, so
T3 through T11 land as one working branch state and are committed per task but verified together
at T11. Running the suite between T3 and T11 reports a build failure, not a regression.
