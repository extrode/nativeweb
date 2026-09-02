-- The slug is the key and is not in the SET list: a tag's address does not
-- change under the links that carry it.
UPDATE tags
SET name = @name,
    color = @color
WHERE slug = @slug;
