-- @params pattern:string, afterTitle:string, afterId:string, take:int
-- Ordered by title, mirrored. Serves a descending list walking forwards and an
-- ascending list walking backwards; see ListByKeyDesc.sql.
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
  AND (@afterId = '' OR (title, id) < (@afterTitle, @afterId))
ORDER BY title DESC, id DESC
LIMIT @take;
