-- The mirror of ListByKeyAsc.sql: DESC, and the boundary comparison flipped.
-- @params pattern:string, afterId:string, take:int
SELECT
    slug,
    name,
    color
FROM tags
WHERE name ILIKE @pattern
  AND (@afterId = '' OR slug < @afterId)
ORDER BY slug DESC
LIMIT @take;
