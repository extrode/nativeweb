-- @params pattern:string
-- @first
SELECT COUNT(*) AS total
FROM tags
WHERE name ILIKE @pattern;
