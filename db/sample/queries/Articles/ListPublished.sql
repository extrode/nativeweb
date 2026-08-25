-- @params take:int
-- @allow-sort demo table, three seeded rows; an index would cost more to explain than the sort costs to run
-- The public page's feed: published articles only, newest first.
--
-- The directive on line 2 is the interesting line in this file, and it is here
-- rather than an index deliberately. `articles` carries exactly one index, the
-- id primary key, so ORDER BY created_at raises JNT8007 -- "this query sorts at
-- runtime". JauntyQ rates that a Warning; TreatWarningsAsErrors is on repo-wide,
-- so here it stops the build.
--
-- On a table that grows, the answer is the index, and four migrations in
-- db/migrations/ are exactly that (0023, 0033, 0034, 0048). This table does not
-- grow: it is three seeded rows in a demo, and PostgreSQL would ignore the index
-- at that size anyway. So the honest answer is to accept the sort and say why,
-- which is what @allow-sort requires -- the reason is mandatory, and a bare
-- directive is rejected outright with JNT3008.
--
-- The directive is also self-expiring. Add an index whose leading column is
-- created_at and JNT8012 fires on this line: "declared but no ORDER BY column on
-- this query is unindexed, so it suppresses nothing." An exemption cannot quietly
-- outlive the thing that justified it.
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
