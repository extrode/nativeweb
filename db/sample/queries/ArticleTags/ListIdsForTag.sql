-- @params tagSlug:string
-- The articles one tag holds, as the admin edit form reads them (spec 023 R2):
-- ids only, in id order, the reverse of ListSlugsForArticle.sql, through the
-- tag row for the same reason that file goes through the article row. Served
-- by article_tags_tag_slug_article_id_idx.
SELECT at.article_id
FROM tags t
JOIN article_tags at ON at.tag_slug = t.slug
WHERE t.slug = @tagSlug
ORDER BY at.article_id;
