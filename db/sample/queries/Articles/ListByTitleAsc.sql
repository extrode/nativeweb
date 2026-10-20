-- @params pattern:string, afterTitle:string, afterId:string, take:int
-- Ordered by title, walking forwards.
--
-- (title, id) > (@afterTitle, @afterId) is a row-value comparison, and it is the
-- whole reason the key is a tiebreaker rather than decoration: it decomposes to
-- title > @afterTitle OR (title = @afterTitle AND id > @afterId), so a block of
-- rows sharing one title has a single unambiguous boundary inside it. Ordering by
-- title alone would give a page boundary two valid answers, and rows would repeat
-- on one page and be missing from the next.
--
-- PostgreSQL serves this from an index on (title, id) without a sort node, and
-- LIMIT stops the scan early. See 0002_scaffold_sort_indexes.sql.
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
WHERE (title ILIKE @pattern
    OR COALESCE(author, '') ILIKE @pattern
    OR body ILIKE @pattern)
  AND (@afterId = '' OR (title, id) > (@afterTitle, @afterId))
ORDER BY title, id
LIMIT @take;
