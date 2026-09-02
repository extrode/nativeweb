-- @first
SELECT
    slug,
    name,
    color
FROM tags
WHERE slug = @slug;
