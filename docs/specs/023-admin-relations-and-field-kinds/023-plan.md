# 023 — Plan
Spec: ./023-spec.md

## Approach
The generator learns four property kinds from the model and nothing from the sample: an enum, a
`DateOnly`, a `string?` marked `[ScaffoldReference(typeof(T))]`, and an `IReadOnlyList<string>`
marked `[ScaffoldRelation(typeof(T))]` with a control choice. Each renders its own control in the
emitted `FormHtml`, binds through the emitted form record, and converts through `ScaffoldValues`
like every type before it. The options a reference or relation offers are the target's rows,
read through the target's own `IScaffoldStore<T>` from the generated handler; the target must be
a `[Scaffold]` model in the same compilation, which the pipeline already collects before it
emits, so the target's key and label property are known at generation time.

Two decisions came out of the design critique. First, **the store is the authority on whether
a posted key exists**, not the handler: the SQL store maps a foreign-key violation to
`ScaffoldRefusedException` the way it already maps the slug's unique violation, and the memory
store checks the other store under its save lock. The handler validates only what it can know
from the model (an enum member, a date) and renders the options it loaded; a row's currently
held keys are always rendered even when past the option limit. Validating against a loaded
snapshot was rejected: it fails a valid key past the limit and leaves a window between the
snapshot and the write that the database would have closed anyway. Second, **both sides of the
relation are editable**, as the spec asks, and each save rewrites its own side's links inside a
transaction. Two admins saving overlapping sides at once is last-write-wins on the overlap, the
same as two admins editing one row today; it is documented, not prevented.

The sample changes follow: `Published` becomes `ArticleStatus Status` stored as text with a
CHECK, `PublishedAt` is a nullable `DateOnly` stored as `DATE`, `Category` is a third scaffolded
model with its own table and `articles.category_key REFERENCES categories ON DELETE SET NULL`,
`Article.TagSlugs` and `Tag.ArticleIds` carry the relation on both paths, and `ViewCount` is
`[ScaffoldReadOnly]`. Migration 0004 rehearses on a copy first, as 0003 did.

## Tech / dependencies
No new packages. .NET 10, NativeAOT, JauntyQ from the local checkout at `../jauntyq`. JauntyQ maps
a `DATE` column to `System.DateTime` (`DialectMapper.cs:314`), so the SQL store converts to and
from `DateOnly`; `JauntyDb.BeginTransactionAsync` exists for the join-table rewrite. The schema
snapshot `db/sample/schema/sample.schema.json` is re-pulled after 0004 is applied, with the
command in `db/sample/README.md`. Generated code stays at C# 9 (no collection expressions;
`Array.Empty<string>()`). Applying 0004 to the two local databases is on the confirmation list
and is asked for at that task.

## Changes by area
- `libs/nativeweb.scaffold/ScaffoldAttributes.cs` (R6) — `ScaffoldReferenceAttribute(Type target)`,
  `ScaffoldRelationAttribute(Type target) { ScaffoldRelationControl Control }`, enum
  `ScaffoldRelationControl { Checkboxes, MultiSelect }`. XML docs state the property types each
  accepts and that the target must be scaffolded.
- `libs/nativeweb.scaffold/ScaffoldValues.cs` (R6, R7) — `IsDateOnly`, `DateOnlyValue(raw, fallback)`,
  `NullableDateOnly`, `Editable`/`Display` arms for `DateOnly` (`yyyy-MM-dd`) and for enums
  (member name spaced); `IsEnumMember<T>(raw)`, `EnumValue<T>(raw, fallback)`,
  `NullableEnum<T>(raw)` using `Enum.TryParse<T>` (AOT-safe, constrained generic); `Keys(string[])`
  trims and drops blanks.
- `libs/nativeweb.scaffold.generator/ScaffoldModel.cs` (R6) — `PropertyModel` gains `IsEnum`,
  `EnumMembers` (names), `Reference` and `Relation` (target type name), `RelationControl`,
  `IsCollection`; all in `Equals` and `GetHashCode`, per the `Sortable` precedent.
- `libs/nativeweb.scaffold.generator/ScaffoldGenerator.cs` (R6) — read enum members from the
  type symbol; read the two new attributes; compute `ReadOnly` from `Convert` **after** the
  attribute loop, since `Convert` now depends on the relation flag; diagnostics for: a reference
  or relation target that is not a `[Scaffold]` model in the compilation, a reference on a
  property that is not `string?`, a relation on a property that is not `IReadOnlyList<string>`,
  and a target whose key is not a string.
- `libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs` (R6, R7) — form record fields are
  `string[]` for relation properties (`form["x"].ToArray()`), `string?` otherwise;
  `NewPage`/`EditAsync`/`CreateAsync`/`UpdateAsync` load an emitted `{Model}Options` (one
  `IReadOnlyList<Target>` per reference or relation, from `IScaffoldStore<Target>.ListAsync` with
  `OptionLimit` 500) and pass it to `FormHtml`; `NewPage` becomes async and the route wiring
  follows; `FromForm` validates enum membership and date shape with the R7 messages;
  `Build` converts enums, dates, references (`Text`) and relations (`Keys`); `FormHtml` emits
  `<select>` for enums (blank option when nullable) and references (blank "(none)" option),
  `<input type="date">`, a `<fieldset>` of `<input type="checkbox" name="x" value="key">` or a
  `<select multiple name="x">`, selected state from `submitted` when present else the row, a held
  key not among the options rendered as a selected option labelled by its key; list cells display
  enums and dates through `Display`; option label is the target's first non-key string
  `[ScaffoldList]` property, else its key.
- `apps/sample/Models.cs` (R1–R5, R8) — `enum ArticleStatus { Draft, Published, Archived }`;
  `Article`: `Status` (listed), `DateOnly? PublishedAt` (listed, label "Published"), `string?
  CategoryKey` with `[ScaffoldReference(typeof(Category))]` (listed, label "Category"),
  `IReadOnlyList<string> TagSlugs` with `[ScaffoldRelation(typeof(Tag))]`, `ViewCount`
  `[ScaffoldReadOnly]`; `Tag`: `IReadOnlyList<string> ArticleIds` with
  `[ScaffoldRelation(typeof(Article), Control = MultiSelect)]`; new `[Scaffold] [Auth(Role =
  "admin")] Category { [ScaffoldKey] Key; [ScaffoldList][ScaffoldSearch][ScaffoldSort] Name }`.
- `apps/sample/DemoStore.cs` (R1, R2, R5, R7, R9) — `MemoryStore<T>` gains a virtual `OnDelete(key)`
  hook and `DeleteAsync` calls it; `MemoryRelation` gains `ArticlesOf(tagSlug)`,
  `ReplaceTagsOf(articleId, slugs)`, `ReplaceArticlesOf(tagSlug, ids)`, `RemoveTag(slug)`,
  `RemoveArticle(id)`, and its remarks change from "nothing cascades" to what now does;
  `ArticleStore` takes the relation, `TagStore` and `CategoryStore`, refuses an unknown tag slug or
  category key in `BeforeSave` with the R7 message, writes the relation after the save, and
  cascades on delete; `TagStore` takes the relation and `ArticleStore`, refuses an unknown article
  id, writes the reverse side, cascades on delete; new `CategoryStore` clears `CategoryKey` on
  every article in `OnDelete`; `GetAsync` on both fills the list property from the relation.
  `TagStore.AssignKey` keeps a typed slug (R9).
- `apps/sample/JauntyArticleStore.cs`, `JauntyTagStore.cs`, new `JauntyCategoryStore.cs`
  (R1–R5, R7, R9) — status and date mapped in both directions; `GetAsync` adds one query for the
  links; create and update run insert/update plus the join rewrite in one transaction; a
  foreign-key violation (SQL state 23503) on `article_tags` or `category_key` becomes
  `ScaffoldRefusedException` with the field's message, beside the existing 23505 mapping;
  `JauntyTagStore` insert keeps the typed slug.
- `db/sample/migrations/0004_status_dates_categories.sql` (R3, R4, R5) — `categories (key TEXT PRIMARY
  KEY CHECK slug alphabet, name TEXT NOT NULL)` with a `(name, key)` index; `articles.status TEXT
  NOT NULL DEFAULT 'draft'` backfilled from `published` then `CHECK (status IN ('draft',
  'published', 'archived'))` and `published` dropped; `articles.published_at DATE NULL`;
  `articles.category_key TEXT NULL REFERENCES categories (key) ON DELETE SET NULL`; an index
  `(published_at DESC NULLS LAST, created_at DESC, id DESC)` for the public order; the
  `ListPublished*`, `CountView`, `GetBySlug`, `ListForArticle`, `ListForWindow`, `ListWithCounts`
  and the 8 admin `ListBy*` queries read `status = 'published'` in place of `published`.
- `db/sample/queries/` (R1–R5) — `Articles/{Insert,Update,GetById,GetBySlug,ListBy*}.sql` carry
  `status`, `published_at`, `category_key`; `ArticleTags/{ListSlugsForArticle,ListIdsForTag,
  DeleteForArticle,DeleteForTag,Insert}.sql`; `Categories/{Count,Delete,GetById,Insert,ListByKeyAsc,
  ListByKeyDesc,ListByNameAsc,ListByNameDesc,Update,ListAll}.sql`; public ordering
  `ORDER BY published_at DESC NULLS LAST, created_at DESC, id DESC`.
- `apps/sample/PublishedArticles.cs`, `PublicTags.cs`, `PublicDocuments.cs`,
  `PublicArticleEndpoints.cs` (R3, R4) — memory readers filter `Status == Published` and order by
  `PublishedAt ?? DateOnly.FromDateTime(CreatedAt)` then `CreatedAt` then id descending;
  `PublishedArticle` carries `PublishedAt`; feed dates and JSON/CSV columns use it where they used
  `CreatedAt` for the published date.
- `apps/sample/Program.cs` (R1, R5) — construct and register the category store on both paths,
  wire the relation into the memory stores, seed one category ("Guides") and give `a1` to it;
  `MapCategoryScaffold` is emitted and mapped by `MapScaffolding`.
- `libs/nativeweb.scaffold/README.md` (R11) — the attribute table gains the two attributes and
  the three type-driven controls; a section "Choosing a control for a relation": checkbox list
  for a short, stable set the admin scans (tags), multi-select for a long list the admin
  searches with the keyboard (articles), and that both are plain HTML with no script.
- `docs/00-quick-start/tutorial-first-feature.md`, `db/sample/README.md` (R11) — the category
  and status in the walkthrough; the schema and the migration list.
- `work/todo.md` — strike the three 022-review lines this spec closes (view count posted back,
  tag delete cascade disagreement, `AssignKey` overwriting a typed slug).

## Data / interfaces
- `IScaffoldStore<T>` is unchanged. The generated handler resolves `IScaffoldStore<Target>` for
  each target; a host that scaffolds a reference without registering the target's store fails at
  the first form request with the usual "no service registered" error, stated in the README.
- Form contract: a relation field posts zero or more `name=key` pairs; none posted means the
  empty set. Reference and enum fields post one value; blank means null for a nullable property.
- Refusal messages (R7): "{Label} must be one of {members}.", "{Label} must be a date.",
  "{Label} has a choice that does not exist." The last comes from the store on both paths.
- `article_tags` unchanged; `categories` and the three `articles` columns as above.

## Risks & mitigations
- **Overlapping saves on the two relation sides** lose the overlap to the later save. Documented in
  the README section and the store remarks; a product wanting better makes one side read-only.
- **Option limit.** A target past 500 rows renders its first 500 by default order plus the row's
  held keys; the limit is a named constant in the emitted code and the README says so.
- **Migration 0004 drops `published`.** Rehearsed on `nativeweb_sample_022probe` (fresh copy of dev)
  before either real database; every query file that read the column is changed in the same task,
  and the JauntyQ build fails on any file still naming it.
- **Enum in AOT.** `Enum.TryParse<T>` and `ToString()` on a boxed enum are supported under
  NativeAOT; the Release publish with zero warnings is the gate, as in 022.
- **`DateOnly` through JauntyQ as `DateTime`.** Writes go through `@publishedAt::date`; reads take
  `DateOnly.FromDateTime`. The integration tier asserts a round trip.
- **Generator diagnostics.** Each new diagnostic has a unit test that compiles a bad model and
  asserts the id, so a consumer's mistake is a build error with a message rather than emitted
  code that does not compile.
- **Existing tests post `published` and `viewCount`.** `JauntyStoreTests`, `ScaffoldedCrudTests`,
  `ScaffoldedDefectTests`, `JauntySlugAndTagTests`, `PublicSiteTests`, the e2e admin tests and
  `SampleTestDatabase.InsertAsync` change with the model in the same task.
- **Data exposure.** Archived joins Draft on every public read, in SQL (`status = 'published'`)
  and memory, before mapping; the three-way 404 test gains an archived case.
- **Input handling.** Every option value is the target's key, HTML-encoded on render and never
  trusted on post: the store is the check. Checkbox and select controls carry no script.

## Coverage check
| R# | Plan section |
| --- | --- |
| R1 | Article `TagSlugs` relation, checkbox control, both stores' join write |
| R2 | Tag `ArticleIds` relation, multi-select control, reverse write, shared relation |
| R3 | `ArticleStatus`, migration backfill and CHECK, readers filter, query rewrite |
| R4 | `PublishedAt`, `DATE` column, index and public order, feed/JSON/CSV dates |
| R5 | `Category` model, table, `ON DELETE SET NULL` and memory `OnDelete`, reference dropdown |
| R6 | attributes, generator model and diagnostics, emitter controls, `ScaffoldValues` |
| R7 | `FromForm` enum and date checks; store refusals for unknown keys on both paths |
| R8 | `[ScaffoldReadOnly]` on `ViewCount`, `Build` copies it from `existing` |
| R9 | `MemoryRelation.RemoveTag`, SQL cascade already present, `AssignKey` keeps typed slug |
| R10 | Verification below |
| R11 | README section, tutorial, `db/sample/README.md` |

## Verification
- `tests/nativeweb.scaffold.tests.unit`: generator emits the four controls for a test model with
  an enum, a `DateOnly?`, a reference and two relations (one per control); each diagnostic fires
  on its bad model; `ScaffoldValues` date and enum conversions, including "2026-02-30" and an
  unknown member; the emitted handler with fake stores renders options, keeps a held key not in
  the options, re-renders with the R7 messages, and treats no checkbox as the empty set.
- `tests/nativeweb.sample.tests.unit`: memory stores refuse unknown keys, write both relation
  sides, cascade on tag and category delete, keep a typed tag slug; readers hide Archived and
  order by `PublishedAt` with the fallback.
- `tests/nativeweb.scaffold.tests.integration` on both factories: the acceptance criteria as
  written (tick tags then tag pages; deselect from the tag side; archive then 404 identical to
  unknown; later date leads; category pick, delete, "none"; three invalid posts leave the row
  unchanged; view count 43 after 3 visits with the form open); migration backfill asserted on the
  SQL factory against rows inserted with each status.
- `tests/nativeweb.scaffold.tests.e2e`: Chromium ticks two tags on an article, saves, follows the
  tag link on the public page; opens the tag, deselects the article in the multi-select, saves;
  picks a category from the dropdown; sets Archived and sees the 404.
- Each new test shown red with its code broken, then `git checkout --`; every suite green; Release
  AOT publish with zero warnings; structure audit 0 errors; two-lens `/code-review`, CONFIRMED
  findings fixed; merge `--no-ff` into `dev`, unpushed.
