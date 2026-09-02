-- @params pattern:string, afterId:string, take:int
SELECT
    key,
    name
FROM categories
WHERE name ILIKE @pattern
  AND (@afterId = '' OR key < @afterId)
ORDER BY key DESC
LIMIT @take;
