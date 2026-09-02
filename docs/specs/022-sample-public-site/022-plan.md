# 022 — Plan
Spec: ./022-spec.md

## Approach
Everything lands in `apps/sample`, `db/sample` and the tests, plus one seam in the scaffold
generator. The public pages are compiled templates under `apps/sample/templates/` rendered through
the existing `Templates.*` types and `layout/page`, the same way `/articles` is rendered today;
the data comes through two read seams beside `IPublishedArticles` so the endpoints never learn
which store answers. Tags and the article-tag relation become two tables on the database path,
with a `JauntyTagStore` replacing the in-memory `TagStore` there, so the existing tag admin
persists. Slugs are a new column on `articles`, backfilled set-based in the migration, derived and
validated in the article stores, and refused through a new `ScaffoldRefusedException` that the
emitted create and update handlers catch and render as the form's error message.

Two alternatives were rejected. Deriving the slug silently in the store and never refusing (no
framework change) fails the user story: an admin choosing a slug another article holds must be
told, not overwritten with a suffix. Keeping tags in memory on the database path and reading
only a join table from PostgreSQL fails R4: an admin edit would not persist where the public page
reads.

Two spec lines are amended here rather than planned around, each recorded in the spec:
- R4 asked both paths to seed article-tag rows. The database path seeds no articles at all by
  design (the tests mint their own rows; `db/sample/README.md`), so the migration seeds the tags
  and the integration tests create the relations they assert on.
- The non-goal "any change to the framework libraries" becomes "beyond the one refusal seam".
  `ScaffoldEmitter.EmitCreate` and `EmitUpdate` call the store and redirect; nothing in
  `IScaffoldStore<T>` can say no. The seam is generic (a store refuses a save with a message) and
  on the Django side of the line.

## Tech / dependencies
No new packages. .NET 10, NativeAOT, JauntyQ from the local checkout at `../jauntyq` (the
`Directory.Build.props` probe). The schema snapshot `db/sample/schema/sample.schema.json` must be
re-pulled after the migration is applied to `nativeweb_sample_dev`, with the CLI at
`../jauntyq/src/JauntyQ.Cli/bin/Release/net8.0/JauntyQ.Cli.exe` and the command in
`db/sample/README.md`. Applying the migration to the two local databases is on the confirmation
list and is asked for at that step, not assumed.

## Changes by area
- `db/sample/migrations/0003_slugs_and_tags.sql` (R2, R4) — `ALTER TABLE articles ADD COLUMN slug
  TEXT`; backfill: `lower(title)` with every run of characters outside `[a-z0-9]` replaced by one
  hyphen and leading/trailing hyphens trimmed, empty becoming `article`; where two rows share a
  base, every row but the first (by `created_at, id`) gets `-<id>` appended, unique by
  construction; then `SET NOT NULL`, a `CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')`, and a unique
  index `articles_slug_key`. `CREATE TABLE tags (slug TEXT PRIMARY KEY CHECK (same pattern), name
  TEXT NOT NULL, color TEXT NULL)`. `CREATE TABLE article_tags (article_id TEXT REFERENCES
  articles ON DELETE CASCADE, tag_slug TEXT REFERENCES tags ON DELETE CASCADE, PRIMARY KEY
  (article_id, tag_slug))` with an index on `(tag_slug, article_id)` for the tag page. Seed the
  three tags `aot`, `htmx`, `postgres` with `ON CONFLICT DO NOTHING`. Idempotent throughout, as
  0001 and 0002 are.
- `db/sample/queries/Articles/` (R2, R3, R5, R6, R8) — `Insert.sql` and `Update.sql` gain `slug`;
  `GetById.sql`, `ListPublished.sql` and the eight keyset lists select it. New: `GetBySlug.sql`
  (`@first`, `WHERE slug = @slug AND published`), `CountView.sql` (`UPDATE ... SET view_count =
  view_count + 1 WHERE slug = @slug AND published`, rows affected is the answer),
  `ListPublishedByTag.sql` (join through `article_tags`, same order and `LIMIT @take`),
  `ListPublishedForSitemap.sql` (`slug, created_at` for every published row, newest first).
- `db/sample/queries/ArticleTags/` (R3, R5, R8) — `ListForRecent.sql` (tag slug and name for
  every article in the newest-`@take` published window, the window as a subquery so no array
  parameter is needed), `ListForTag.sql` (the same for the tag page's window),
  `ListForArticle.sql` (one article's tags by id).
- `db/sample/queries/Tags/` (R4, R5) — the scaffold set `JauntyTagStore` needs: `ListByKeyAsc`,
  `ListByKeyDesc`, `ListByNameAsc`, `ListByNameDesc` (search on `name`, keyset on `(name, slug)`
  or `slug`), `Count`, `GetById`, `Insert`, `Update`, `Delete`; and the public
  `ListWithCounts.sql` (every tag with `COUNT` of published articles, by name) and
  `GetBySlug.sql`.
- `db/sample/migrations/0002_scaffold_sort_indexes.sql` is untouched, but `.github/workflows/ci.yml`
  applies only 0001 today; the provisioning step becomes a loop over `db/sample/migrations/*.sql`
  so 0002 and 0003 both apply (the 0002 omission is pre-existing and gets a changelog line).
  `db/sample/README.md` setup commands become the same loop.
- `libs/nativeweb.scaffold/ScaffoldRefusedException.cs` (R2) — `sealed class
  ScaffoldRefusedException(string message) : Exception`. XML doc: a store throws it from
  `CreateAsync` or `UpdateAsync` to refuse a save with a message the form shows; anything else a
  store throws is still a 500.
- `libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs` (R2) — `EmitCreate` and `EmitUpdate`
  wrap the store call in `try { } catch (ScaffoldRefusedException refused) { return
  ScaffoldLayout.Page(http, ..., FormHtml(http, existing, action, label, refused.Message, form)); }`,
  reusing the exact rendering the `FromForm` error path uses. `libs/nativeweb.scaffold/README.md`
  documents the seam in "what the host still owns".
- `apps/sample/Models.cs` (R2) — `Article` gains `[ScaffoldList] public string? Slug`. Nullable so
  the generated `FromForm` does not require it; the stores derive it when blank.
- `apps/sample/Slugs.cs` (R2, R9) — `Slugs.Derive(title)` and `Slugs.IsValid(candidate)`, the one
  definition of the alphabet; `Slugs.Pattern` is the regex string the route constraint and the
  migration's CHECK repeat.
- `apps/sample/DemoStore.cs` (R2, R4, R6) — `ArticleStore.CreateAsync`/`UpdateAsync` override to
  derive a blank slug from the title (numeric suffix `-2`, `-3` on collision), keep the existing
  slug on update when the submitted one is blank, and throw `ScaffoldRefusedException` on an
  invalid or duplicate slug. A `MemoryRelation` (article id to tag slugs, `ConcurrentDictionary`)
  lives beside `ArticleStore` so `MemoryPublishedArticles` and the memory tag reads share it.
- `apps/sample/JauntyArticleStore.cs`, new `apps/sample/JauntyTagStore.cs` (R2, R4) — slug on
  insert and update with the same rules; a unique-violation on `articles_slug_key`
  (`PostgresException.SqlState == "23505"`) becomes the same refusal message rather than a
  pre-check, so two concurrent creates cannot both pass. `JauntyTagStore` is the article store's
  shape over the `Tags/` queries; `Program.cs` registers it on the database path instead of
  `TagStore`.
- `apps/sample/PublishedArticles.cs`, new `apps/sample/PublicTags.cs` (R1, R3, R5, R6, R8) —
  `PublishedArticle` gains `Slug` and `Tags` (`IReadOnlyList<TagRef>`, `TagRef(Slug, Name)`);
  new `ArticleDetail(Slug, Title, Author, CreatedAt, Body, Tags)`. `IPublishedArticles` gains
  `BySlugAsync(slug)` (published only, null otherwise), `CountViewAsync(slug)` (true when a
  published row was counted), `ByTagAsync(tagSlug, take)`, `SitemapAsync()`. `IPublicTags` has
  `ListAsync()` returning `TagSummary(Slug, Name, PublishedCount)` and `GetAsync(slug)`. Both
  implemented twice (memory, Jaunty). The memory view count is a `lock` around the increment on
  the stored `Article` instance; an admin edit that replaces the instance between its own GET and
  POST loses views counted in that window, said in the doc comment and in `work/todo.md`.
- `apps/sample/PublicSite.cs` (R1, R3, R5, R6, R7, R9) — `MapPublicSite`: `GET /` home,
  `GET /tags`, `GET /tags/{slug}` each `.RequireRateLimiting(General).AsPublicPage(csp)` with the
  templates rendered with an empty antiforgery token; `GET /articles/{slug}` rate-limited and
  uncached: `CountViewAsync` then `BySlugAsync`, 404 when either says no. Route constraints
  `{slug:regex(...)}` using `Slugs.Pattern` on both slug routes so an invalid slug is a 404 before
  the handler. The existing `/articles.{format}` route is a different segment and does not
  collide. `Program.cs` drops the `/` redirect and calls `MapPublicSite`.
- `apps/sample/templates/` (R1, R3, R5) — `site/home.html`, `articles/show.html`,
  `tags/index.html`, `tags/show.html`; `articles/results.html` gains the link to
  `/articles/{{article.Slug}}` and a tag list per item, so the listing, home and tag pages share
  one shape; `layout/page.html` brand links to `/` and the nav gains Tags. The body is passed as
  `IReadOnlyList<string> Paragraphs` (split on blank lines in C#). New `{{t:...}}` keys in every
  file under `apps/sample/locales/`.
- `apps/sample/PublicDocuments.cs`, `PublicArticleEndpoints.cs`, `SampleJson.cs` (R8) — feed
  entry link `/articles/{slug}`; `SitemapEntries` becomes a method over `SitemapAsync()` and
  `IPublicTags.ListAsync()` adding `/`, `/tags`, every tag page and every published article with
  `LastModified = CreatedAt`; the about text names `/`, `/articles/{slug}` and `/tags`; CSV gains
  `slug` and `tags` (space-joined) columns; `SampleJson` serializes `TagRef`.
- Docs (R10) — the sample walkthroughs in `docs/00-quick-start/getting-started.md`,
  `tutorial-first-feature.md`, `docs/01-architecture/native-web-architecture.md`,
  `progressive-enhancement.md` and the root `README.md` gain the new routes where they list the
  sample's; `docs-site/` rebuilt with `bash scripts/build-docs.sh`. `CHANGELOG.md`, `work/status/`.
- Tests (R11) — see Verification.

## Data / interfaces
```
articles      + slug TEXT NOT NULL UNIQUE CHECK(^[a-z0-9]+(-[a-z0-9]+)*$)
tags          slug PK, name, color
article_tags  (article_id -> articles, tag_slug -> tags) PK, index (tag_slug, article_id)

GET /                       Html   public cache     home
GET /articles/{slug}        Html   uncached         detail; counts a view; 404 unknown/unpublished/invalid
GET /tags                   Html   public cache     tag listing with counts
GET /tags/{slug}            Html   public cache     tag page; 404 unknown/invalid

PublishedArticle(Id, Slug, Title, Author, CreatedAt, Tags)
ArticleDetail(Slug, Title, Author, CreatedAt, Body, Tags)
TagRef(Slug, Name)   TagSummary(Slug, Name, PublishedCount)
ScaffoldRefusedException(message)  thrown by a store's CreateAsync/UpdateAsync
```
JSON listing item: `{id, slug, title, author, createdAt, tags:[{slug,name}]}`. CSV columns:
`id, slug, title, author, created, tags`.

## Risks & mitigations
- **Migration on a populated table.** `nativeweb_sample_test` holds ~46k accumulated rows. The
  backfill is one set-based UPDATE with a window function, not a loop; the unique index builds
  after it. A base that already ends in `-<id>` of another row is the only collision the suffix
  cannot avoid; the unique index makes that loud, not silent.
- **Existing tests insert without a slug.** `SampleTestDatabase.InsertAsync` and any e2e sweep
  that writes rows must supply one; the helper uses the id as the slug. Found by the NOT NULL
  constraint on first run, fixed in the same task as the migration.
- **Sitemap size.** Every published row is listed; the test database's 46k rows stay under the
  50k-per-file limit. A sitemap index for larger sites is a `work/todo.md` line.
- **Route constraint versus the negotiated route.** `/articles/{slug}` and `/articles.{format}`
  differ at the segment boundary; the integration tier asserts both still answer.
- **Public cache on template pages.** Home and tag pages render with an empty token and no
  cookie; `PublicPageCache.Csp([])` hashes no inline script, and the layout has none. The cache
  test asserts the second request is served from the store, as the feed test does.
- **Concurrent slug creates on PostgreSQL.** No pre-check; the unique violation is the refusal.
  In memory, derivation and insert happen under the store's dictionary semantics; a test drives
  concurrent creates with the same title and asserts every slug is distinct.
- **Data exposure.** Unpublished rows are excluded in SQL (`AND published`) and in memory before
  any mapping; the 404 for unpublished and unknown is the same `Results.NotFound()`.
- **Input handling.** Slugs reach the store only through the route constraint or the admin form;
  both are validated against `Slugs.Pattern`; every rendered value is template-encoded.

## Coverage check
| R# | Plan section |
| --- | --- |
| R1 | `PublicSite.cs` home, `templates/site/home.html`, layout nav |
| R2 | migration, `Slugs.cs`, `Models.cs`, both article stores, refusal seam |
| R3 | `GetBySlug.sql`, `BySlugAsync`, `articles/show.html`, same 404 |
| R4 | migration tables and tag seeds, `JauntyTagStore`, `MemoryRelation`, seeds in `Program.cs` |
| R5 | `Tags/ListWithCounts.sql`, `IPublicTags`, `tags/*.html`, `ListPublishedByTag.sql` |
| R6 | `CountView.sql`, `CountViewAsync`, memory lock, concurrency test |
| R7 | `AsPublicPage` on home and tags, none on detail, cache tests |
| R8 | feed link, sitemap method, about text, JSON and CSV columns, results link |
| R9 | route constraints, rate limit on every route, template encoding, security-header test |
| R10 | docs list, `build-docs.sh`, `DocsClaimsTests` |
| R11 | Verification below |

## Verification
- `tests/nativeweb.sample.tests.unit`: `SlugTests` (derive: `Hello, World! (Again)` →
  `hello-world-again`, empty → `article`, validity table), `ArticleStoreSlugTests` (derive on
  create, keep on title edit, refuse duplicate and invalid, distinct under 50 concurrent creates
  of one title), `PublishedArticlesTests` additions (by slug published only, view count 50 under
  parallel calls, missing slug counts nothing, by tag, sitemap), `PublicTagsTests` (counts
  exclude unpublished, zero for an empty tag), template render tests for the empty states.
- `tests/nativeweb.scaffold.tests.unit`: the emitter's generated source contains the catch, and
  a fake store throwing `ScaffoldRefusedException` yields a 200 form with the message on both
  create and update.
- `tests/nativeweb.scaffold.tests.integration/PublicSiteTests.cs` on both factories: home 200 with
  every seeded published article linked and no unpublished one; detail 200 with tags; unknown,
  unpublished and invalid slugs all 404 with equal status and headers; view count via the admin
  page after 50 concurrent GETs; home served from cache on the second request and detail never
  marked cacheable; feed, sitemap, JSON and CSV carry the slug links; admin create with blank
  slug derives it, duplicate slug re-renders the form with the message and changes nothing;
  `/articles.json` still answers beside `/articles/{slug}`.
- `tests/nativeweb.scaffold.tests.e2e/PublicSiteTests.cs`: Chromium on `InMemorySampleFixture`
  from `/` to an article to a tag and back.
- Each new test shown red with its code broken, then `git checkout --`; render, web, scaffold
  unit, scaffold integration, sample unit, sample integration, repo and e2e suites green; Release
  AOT publish with zero warnings; `check.ts` intact; structure audit 0 errors; two-lens
  `/code-review` on the branch, CONFIRMED findings fixed; merge `--no-ff` into `dev`, unpushed.
