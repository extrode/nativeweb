-- @params pattern:string, afterCreated:DateTime, afterId:string, take:int
-- Ordered by created_at, mirrored. Newest first is what an admin list of articles
-- is usually asked for, so this is the one of the eight most likely to be hot.
-- Serves a descending list walking forwards and an ascending list walking
-- backwards; see ListByKeyDesc.sql.
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
  AND (@afterId = '' OR (created_at, id) < (@afterCreated, @afterId))
ORDER BY created_at DESC, id DESC
LIMIT @take;
