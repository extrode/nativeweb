-- @params pattern:string, afterCreated:DateTime, afterId:string, take:int
-- Ordered by created_at, walking forwards.
--
-- The cursor carries the timestamp in round-trip form ("o"), not the display form.
-- ScaffoldValues.Display formats to the minute, and two rows a second apart would
-- then compare equal on the boundary -- so whatever sat there would repeat on one
-- page and be missing from the next. ScaffoldValues.CursorText exists for this.
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
  AND (@afterId = '' OR (created_at, id) > (@afterCreated, @afterId))
ORDER BY created_at, id
LIMIT @take;
