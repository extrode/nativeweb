-- @params tagSlug:string
-- The tag side of DeleteForArticle.sql, for a save from the tag form.
DELETE FROM article_tags
WHERE tag_slug = @tagSlug;
