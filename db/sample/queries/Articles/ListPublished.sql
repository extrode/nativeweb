-- @params take:int
-- The public page's feed: published articles only, newest first.
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
-- whole-table projection makes the generated row type JauntyQ.Generated.Article,
-- the same type List.sql already produces, so this query adds a method and not a
-- second row shape to learn. Narrow it when the row count makes the width cost
-- something; on a demo table it buys nothing and costs a type.
--
-- ORDER BY created_at DESC, id -- the id tiebreak matters because created_at
-- defaults to NOW() and a seeded or scripted insert can give several rows the
-- same timestamp, which would make the page order unstable between requests.
SELECT
    id,
    title,
    author,
    body,
    published,
    view_count,
    created_at
FROM articles
WHERE published
ORDER BY created_at DESC, id
LIMIT @take;
