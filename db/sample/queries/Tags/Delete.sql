-- article_tags rows go with it, by ON DELETE CASCADE in 0003.
DELETE FROM tags
WHERE slug = @slug;
