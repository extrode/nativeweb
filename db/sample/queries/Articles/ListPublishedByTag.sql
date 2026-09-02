-- @params tagSlug:string, take:int
-- The tag page's window (spec 022 R5): the newest published articles carrying
-- one tag, in the order ListPublished.sql uses. The relation is read from the
-- tag side, served by article_tags_tag_slug_article_id_idx; the join to tags is
-- on its primary key and is what makes this one set-based read rather than the
-- per-row lookup JNT8008 warns about.
SELECT
    a.id,
    a.title,
    a.author,
    a.body,
    a.published,
    a.view_count,
    a.created_at,
    a.slug
FROM tags t
JOIN article_tags at ON at.tag_slug = t.slug
JOIN articles a ON a.id = at.article_id
WHERE t.slug = @tagSlug
  AND a.published
ORDER BY a.created_at DESC, a.id DESC
LIMIT @take;
