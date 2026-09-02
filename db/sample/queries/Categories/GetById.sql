-- @params key:string
-- @first
SELECT
    key,
    name
FROM categories
WHERE key = @key;
