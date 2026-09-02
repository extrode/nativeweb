-- @params articleId:string, tagSlug:string
-- One link. A key that names no row fails the foreign key (23503), which the
-- store turns into the form's "has a choice that does not exist" (spec 023 R7);
-- the form posts a deduplicated set, so the primary key is never hit from there.
INSERT INTO article_tags (article_id, tag_slug)
VALUES (@articleId, @tagSlug);
