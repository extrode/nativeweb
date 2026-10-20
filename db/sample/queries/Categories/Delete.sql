-- @params key:string
-- ON DELETE SET NULL on articles.category_key does the rest (spec 023 R5).
DELETE FROM categories
WHERE key = @key;
