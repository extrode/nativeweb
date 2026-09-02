-- The mirror of ListByNameAsc.sql.
-- @params pattern:string, afterName:string, afterId:string, take:int
SELECT
    slug,
    name,
    color
FROM tags
WHERE name ILIKE @pattern
  AND (@afterId = '' OR (name, slug) < (@afterName, @afterId))
ORDER BY name DESC, slug DESC
LIMIT @take;
