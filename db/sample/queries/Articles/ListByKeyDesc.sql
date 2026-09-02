-- @params pattern:string, afterId:string, take:int
-- The default order reversed. Serves two callers, which is why there are two files
-- per sortable column and not four: a descending list walking forwards, and an
-- ascending list walking BACKWARDS, whose rows this returns in reverse display
-- order for the store to flip. Both are "the rows on the other side of the
-- boundary", and the mirrored comparison is the same predicate either way.
SELECT
    id,
    title,
    author,
    body,
    published,
    view_count,
    created_at,
    slug
FROM articles
WHERE (title ILIKE @pattern
    OR COALESCE(author, '') ILIKE @pattern
    OR body ILIKE @pattern)
  AND (@afterId = '' OR id < @afterId)
ORDER BY id DESC
LIMIT @take;
