-- @params tagSlug:string?, take:int
-- The tags of every article in a newest-@take published window (spec 022 R1,
-- R5, R8), one row per link: the whole site's window when @tagSlug is null, one
-- tag's when it is not. The window is the same subquery ListPublished.sql and
-- ListPublishedByTag.sql answer, so a page and its tags agree on which articles
-- are in it without an array parameter crossing the wire, and an article on a
-- tag page shows all of its tags, not only the one being browsed. One query
-- rather than two because the two differed only in the window's filter.
SELECT
    at.article_id,
    t.slug,
    t.name
FROM article_tags at
JOIN tags t ON t.slug = at.tag_slug
WHERE at.article_id IN (
    SELECT a.id
    FROM articles a
    WHERE a.published
      AND (@tagSlug IS NULL OR EXISTS (
          SELECT 1
          FROM article_tags w
          WHERE w.article_id = a.id
            AND w.tag_slug = @tagSlug))
    ORDER BY a.created_at DESC, a.id DESC
    LIMIT @take)
ORDER BY at.article_id, t.name, t.slug;
