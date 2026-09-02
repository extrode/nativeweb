-- @params pattern:string, afterId:string, take:int
-- The scaffolded list's default order, keyset-paged by the key alone; the
-- primary key serves it.
SELECT
    key,
    name
FROM categories
WHERE name ILIKE @pattern
  AND (@afterId = '' OR key > @afterId)
ORDER BY key
LIMIT @take;
