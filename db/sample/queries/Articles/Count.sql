-- @params pattern:string
-- Total matching the same predicate List.sql uses, for the pager. The two
-- WHERE clauses must stay identical; a filter applied to the page but not the
-- count produces a pager that offers a page nine that renders empty.
-- @first
SELECT COUNT(*) AS total
FROM articles
WHERE title ILIKE @pattern
   OR COALESCE(author, '') ILIKE @pattern
   OR body ILIKE @pattern;
