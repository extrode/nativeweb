-- @params pattern:string, afterAuthor:string?, afterId:string, take:int
-- Ordered by author, walking forwards. THE NULLABLE ONE, and the only file here
-- whose predicate is not a plain row-value comparison.
--
-- (author, id) > (@afterAuthor, @afterId) evaluates to NULL, not true, for an
-- author-less row. NULL is not true, so WHERE drops it -- and every page after the
-- first would silently omit every row with no author, on a query that looks right
-- and returns rows. On a table small enough to fit one page it never shows at all.
--
-- So the boundary is expressed as two cases, and which one applies is read off
-- @afterAuthor itself:
--
--   @afterAuthor IS NOT NULL  the reader is inside the non-null block. Rows after
--                             them there, plus the whole NULL block, which sorts
--                             after every non-null value under NULLS LAST.
--   @afterAuthor IS NULL      the reader is already inside the NULL block. Only
--                             rows further into it, ordered by the key.
--
-- The cursor can tell those apart because ScaffoldCursor carries a null sort value
-- distinctly from an empty one. A cursor that could not would re-enter the NULL
-- block from its start and repeat every row in it.
--
-- NULLS LAST is PostgreSQL's default for ASC and is stated anyway: the store's
-- in-memory twin has to agree about where an author-less row sits, and a default
-- nobody wrote down is a default somebody changes.
SELECT
    id,
    title,
    author,
    body,
    published,
    view_count,
    created_at
FROM articles
WHERE (title ILIKE @pattern
    OR COALESCE(author, '') ILIKE @pattern
    OR body ILIKE @pattern)
  AND (
        @afterId = ''
     OR (@afterAuthor IS NOT NULL
         AND (author IS NULL OR (author, id) > (@afterAuthor, @afterId)))
     OR (@afterAuthor IS NULL AND author IS NULL AND id > @afterId)
      )
ORDER BY author NULLS LAST, id
LIMIT @take;
