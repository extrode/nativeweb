-- @params pattern:string, take:int, skip:int
-- One page of the scaffolded list, ordered by key so paging is stable.
--
-- The three declarations above are required, not decoration: none of these
-- parameters appears in a comparison against a typed column, so the generator
-- has nothing to infer from and says so (JNT4003) rather than guessing.
--
-- @pattern is an ILIKE pattern, not a search term: the caller passes '%' for an
-- unfiltered list and '%term%' to filter. That is deliberate. The alternative --
-- `WHERE @search IS NULL OR title ILIKE '%' || @search || '%'` -- makes the
-- parameter's type undeterminable to PostgreSQL on the IS NULL branch, so it
-- needs a cast the SQL then has to carry for no gain. One query serves both
-- cases either way.
--
-- COALESCE on author because it is the one nullable column, and NULL ILIKE
-- anything is NULL, not false -- an author-less row would drop out of an
-- unfiltered list without it.
SELECT
    id,
    title,
    author,
    body,
    published,
    view_count,
    created_at
FROM articles
WHERE title ILIKE @pattern
   OR COALESCE(author, '') ILIKE @pattern
   OR body ILIKE @pattern
ORDER BY id
LIMIT @take OFFSET @skip;
