-- @params pattern:string, afterAuthor:string?, afterId:string, take:int
-- Ordered by author, mirrored: NULLS FIRST, then non-null descending. Serves a
-- descending list walking forwards and an ascending list walking backwards.
--
-- The two cases are the same two as ListByAuthorAsc.sql with every comparison
-- flipped, and they change places: under NULLS FIRST the NULL block is what a
-- reader starts in, and leaving it means entering the non-null values from the
-- largest.
--
--   @afterAuthor IS NULL      inside the NULL block. Rows further into it by key,
--                             plus the entire non-null block, which follows it.
--   @afterAuthor IS NOT NULL  past the NULL block. Only smaller non-null values.
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
  AND (
        @afterId = ''
     OR (@afterAuthor IS NULL
         AND (author IS NOT NULL OR (author IS NULL AND id < @afterId)))
     OR (@afterAuthor IS NOT NULL
         AND author IS NOT NULL AND (author, id) < (@afterAuthor, @afterId))
      )
ORDER BY author DESC NULLS FIRST, id DESC
LIMIT @take;
