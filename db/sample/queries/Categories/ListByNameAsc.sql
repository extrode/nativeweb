-- @params pattern:string, afterName:string, afterId:string, take:int
-- Sorted by name with the key as the tiebreak, the keyset rule spec 017 set;
-- categories_name_key_idx matches the ORDER BY. Also the order the article
-- form's category dropdown reads, through the same store.
SELECT
    key,
    name
FROM categories
WHERE name ILIKE @pattern
  AND (@afterId = '' OR (name, key) > (@afterName, @afterId))
ORDER BY name, key
LIMIT @take;
