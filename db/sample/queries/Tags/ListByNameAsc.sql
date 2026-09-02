-- Keyset on (name, slug), served by tags_name_slug_idx; the slug tiebreak is
-- what makes a page boundary inside a run of equal names one answer.
-- @params pattern:string, afterName:string, afterId:string, take:int
SELECT
    slug,
    name,
    color
FROM tags
WHERE name ILIKE @pattern
  AND (@afterId = '' OR (name, slug) > (@afterName, @afterId))
ORDER BY name, slug
LIMIT @take;
