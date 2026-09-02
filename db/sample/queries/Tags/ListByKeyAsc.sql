-- Tags persist on the database path from spec 022 (R4): JauntyTagStore is the
-- second IScaffoldStore<T> over generated queries, and the tag admin edits
-- these rows. Search is on name alone, as [ScaffoldSearch] on the model says.
-- @params pattern:string, afterId:string, take:int
SELECT
    slug,
    name,
    color
FROM tags
WHERE name ILIKE @pattern
  AND (@afterId = '' OR slug > @afterId)
ORDER BY slug
LIMIT @take;
