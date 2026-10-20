-- @params pattern:string, afterName:string, afterId:string, take:int
SELECT
    key,
    name
FROM categories
WHERE name ILIKE @pattern
  AND (@afterId = '' OR (name, key) < (@afterName, @afterId))
ORDER BY name DESC, key DESC
LIMIT @take;
