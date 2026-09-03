-- Plan 009: the demo articles, so the database path shows the site the memory
-- path shows.
--
-- Until now `Seed.Fill` in apps/sample/Program.cs gave the in-memory store three
-- articles and nothing gave the database any, so `dotnet run` against
-- nativeweb_sample_dev served an empty /articles while the same binary with no
-- connection string served three. That is not a missing feature on either side;
-- it is one dataset that was only ever written once. It is written twice now,
-- and the two must agree -- a row changed here is changed there.
--
-- Idempotent by ON CONFLICT DO NOTHING on both tables, so db/migrate.sh can
-- re-run the file, and so a database an admin has already typed into keeps what
-- it has. Deliberately NOT an upsert: an operator who edits the demo copy on
-- nw-dev should not have the edit reverted by the next deploy.
--
-- No conflict target on either statement, and that is the whole point rather
-- than brevity. articles has a second unique index -- articles_slug_key, from
-- 0003 -- and ON CONFLICT (id) suppresses conflicts on the primary key alone,
-- so a row already holding one of these eight slugs under a different id would
-- raise unique_violation. All eight rows are one statement, so that single
-- collision would lose all eight and fail the migration, which on this
-- repository's deploy path means failing the deploy before the colour switch.
-- A bare DO NOTHING covers every unique index the table has now or later.
--
-- DO NOTHING covers unique violations and nothing else, so the tag links are
-- inserted through a SELECT that keeps only the pairs whose article and whose
-- tag both exist. article_tags carries two foreign keys (0003) and articles a
-- third to categories (0004), and each of the three has a reachable trigger on
-- a host an admin has typed into: a tag deleted through the admin, a category
-- deleted the same way, or a hand-made row already holding one of these slugs,
-- which DO NOTHING then skips -- leaving its tag links pointing at an id that
-- is not there. Any of the three raises foreign_key_violation, which
-- ON CONFLICT does not catch; db/migrate.sh runs the file with
-- --single-transaction and ON_ERROR_STOP, so the whole migration rolls back,
-- 0005 never records as applied, and the deploy fails at the same point on
-- every retry until someone edits the database by hand. The guarded form skips
-- the row instead.
--
-- The ids are the memory store's own (a1..a8) rather than generated keys. A
-- demo row is allowed to have a legible id, and the public pages link by slug
-- regardless; PublicSiteTests names a1 and a3 directly.
--
-- What the eight are for, beyond filling a page. The first three are spec 022's
-- R4 shape and must keep it: a1 carries two tags, a2 carries none, and the only
-- article tagged `postgres` (a3) is unpublished, so the tag index has a zero to
-- show and /tags/postgres has an empty state. The five added here carry what
-- the site could not otherwise demonstrate:
--
--   a4  archived -- the third status, absent from every public page and a 404
--       on its own URL, which is the only way to see that Archived and Draft
--       are alike to a visitor and different to the admin. It carries the htmx
--       tag on purpose: the tag index counts published articles, so a4 is the
--       row that proves the count excludes an archived one as well as a draft.
--   a5  a second author, so the by-author sort and the author line are not
--       proved by one name.
--   a6  no category, exercising the (none) option and the list cell that has
--       nothing to show.
--   a7  a long body -- eight paragraphs -- because a reading column cannot be
--       judged on a single sentence.
--   a8  published_at earlier than created_at: an article written today and
--       backdated. It is the row that proves the public order is the
--       publication date rather than the creation time, since ordering by
--       either alone puts it in a different place.
--
-- created_at values are fixed instants, not now(): a demo whose dates move
-- every time the database is built cannot be asserted against, and a screenshot
-- of it ages badly.

INSERT INTO articles (id, title, author, body, view_count, created_at, slug, status, published_at, category_key)
VALUES
    ('a1', 'Lean Native Web', 'Sy',
     'Server-rendered HTML, one native binary, no client build step.',
     1420, '2026-03-04 09:30:00+00', 'lean-native-web', 'published', '2026-03-05', 'guides'),

    ('a2', 'Binding forms under NativeAOT', 'Sy',
     'Complex-type [FromForm] does not compile. IBindableFromHttpContext does.',
     388, '2026-06-18 14:05:00+00', 'binding-forms-under-nativeaot', 'published', '2026-06-20', 'notes'),

    ('a3', 'Scaffolding without reflection', NULL,
     'Draft.',
     0, '2026-08-19 08:00:00+00', 'scaffolding-without-reflection', 'draft', NULL, NULL),

    ('a4', 'The template language we removed', 'Sy',
     'An earlier cut of the renderer had loops, filters and partial inheritance. It was archived when the third nested conditional turned out to be a C# method wearing a costume.' || chr(10) || chr(10) ||
     'What survives is smaller: parameters, a slot, a partial, and an if. Anything that wants more logic than that has a compiler available to it.',
     97, '2026-04-11 11:20:00+00', 'the-template-language-we-removed', 'archived', '2026-04-12', 'notes'),

    ('a5', 'Reading an EXPLAIN before adding an index', 'Dana Whitfield',
     'A keyset page over a nullable column lands in Filter rather than Index Cond, because a disjunction over IS NULL is not a rangeable start point.' || chr(10) || chr(10) ||
     'The fix is not a bigger index. It is knowing which columns are worth paging by, and saying so where the model is declared.',
     512, '2026-07-02 16:45:00+00', 'reading-an-explain-before-adding-an-index', 'published', '2026-07-03', 'guides'),

    ('a6', 'Notes from a week without a bundler', 'Dana Whitfield',
     'No package.json, no lockfile, no node_modules, and a page that loads in one round trip. The vendored libraries are two files and a version comment.',
     264, '2026-07-28 10:15:00+00', 'notes-from-a-week-without-a-bundler', 'published', '2026-07-29', NULL),

    ('a7', 'What a source generator owes you', 'Sy',
     'Reflection is convenient at the cost of a startup you cannot see and a trim warning you cannot fix. A generator moves both into the build, where they are yours to read.' || chr(10) || chr(10) ||
     'The first thing it owes you is legible output. Generated code that no one opens is a second compiler nobody asked for, and when it is wrong the error arrives in a file the author has never seen.' || chr(10) || chr(10) ||
     'The second is a diagnostic instead of a cascade. A model the generator refuses should say why, once, at the declaration -- not produce eleven CS0103s about a class it declined to write.' || chr(10) || chr(10) ||
     'The third is determinism. The same sources produce the same output, in the same order, or the build stops being a fact and becomes a mood.' || chr(10) || chr(10) ||
     'The fourth is a way out. Every generated member should be reachable by hand-written code, and nothing should be reachable only by generated code.' || chr(10) || chr(10) ||
     'None of that is free. The cost is that the generator is now a compiler you maintain, with its own tests, its own diagnostics and its own backwards-compatibility problem.' || chr(10) || chr(10) ||
     'The trade is worth making where the alternative is reflection over types the trimmer is trying to remove. It is not worth making to save typing.' || chr(10) || chr(10) ||
     'Which is the whole argument: a generator earns its place by removing a runtime cost, not by removing a keystroke.',
     1183, '2026-08-05 13:00:00+00', 'what-a-source-generator-owes-you', 'published', '2026-08-06', 'guides'),

    ('a8', 'A note written last week and published today', 'Sy',
     'Backdated on purpose. The public order is the publication date, and this row sits where that date puts it rather than where its creation time would.',
     41, '2026-09-02 07:10:00+00', 'a-note-written-last-week-and-published-today', 'published', '2026-08-24', 'notes')
ON CONFLICT DO NOTHING;

INSERT INTO article_tags (article_id, tag_slug)
SELECT link.article_id, link.tag_slug
FROM (
    VALUES
    ('a1', 'aot'),
    ('a1', 'htmx'),
    ('a3', 'postgres'),
    ('a4', 'htmx'),
    ('a5', 'aot'),
    ('a7', 'aot'),
    ('a8', 'aot'),
    ('a8', 'htmx')
) AS link (article_id, tag_slug)
WHERE EXISTS (SELECT 1 FROM articles WHERE articles.id = link.article_id)
  AND EXISTS (SELECT 1 FROM tags WHERE tags.slug = link.tag_slug)
ON CONFLICT DO NOTHING;
