-- @params take:int
-- The public page's feed: Published articles only, newest publication date first.
--
-- This file used to carry `-- @allow-sort demo table, three seeded rows; an index
-- would cost more to explain than the sort costs to run`, because `articles` had
-- exactly one index -- the id primary key -- so ORDER BY created_at raised JNT8007,
-- "this query sorts at runtime", which TreatWarningsAsErrors turns into a build
-- failure. Accepting the sort and saying why was the honest answer at three seeded
-- rows.
--
-- **The exemption expired on its own, exactly as its own comment predicted.**
-- 0002_scaffold_sort_indexes.sql added (created_at, id) for the scaffolded list's
-- keyset paging, and JNT8012 fired on the directive the next build: "declared but
-- no ORDER BY column on this query is unindexed, so it suppresses nothing." An
-- exemption cannot quietly outlive the thing that justified it, and this one did
-- not -- which is the fact worth keeping here now that the directive is gone.
--
-- @take is declared for the same reason List.sql declares its three: it appears
-- in LIMIT rather than in a comparison against a typed column, so the generator
-- has nothing to infer from and reports JNT4003 instead of guessing.
--
-- Every column is selected even though the page shows three of them. A
-- whole-table projection makes the generated row type
-- Extrode.JauntyQ.Generated.Article, the same type List.sql already produces, so
-- this query adds a method and not a second row shape to learn. Narrow it when the row count makes the width cost
-- something; on a demo table it buys nothing and costs a type.
--
-- The order is the publication date, a row without one dated by its creation
-- day in UTC (spec 023 R4), then created_at, then id: the id tiebreak matters
-- because created_at defaults to NOW() and a seeded or scripted insert can give
-- several rows the same timestamp, which would make the page order unstable
-- between requests. 0004_status_dates_categories.sql carries a partial expression
-- index in this exact shape, all three DESC, over status = 'published' -- a btree
-- can be walked backwards but not half-backwards, so the directions must match
-- the index's own or PostgreSQL sorts at runtime. Every public query that pages
-- this window (ListPublishedByTag, ListPublishedForSitemap, ListForWindow) spells
-- the same expression, so a page and its tags agree on which rows are in it.
--
-- Only Published is public: Draft and Archived are equally absent (R3).
SELECT
    id,
    title,
    author,
    body,
    view_count,
    created_at,
    slug,
    status,
    published_at,
    category_key
FROM articles
WHERE status = 'published'
ORDER BY COALESCE(published_at, (created_at AT TIME ZONE 'UTC')::date) DESC, created_at DESC, id DESC
LIMIT @take;
