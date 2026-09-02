-- @params pattern:string
-- @first
SELECT COUNT(*) AS total
FROM categories
WHERE name ILIKE @pattern;
