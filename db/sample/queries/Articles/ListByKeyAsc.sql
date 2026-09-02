-- @params pattern:string, afterId:string, take:int
-- One page of the scaffolded list in the default order, walking forwards.
--
-- Keyset, not LIMIT/OFFSET. The page is "the rows after this one", so a row
-- inserted ahead of the reader between two requests shifts nothing: with OFFSET,
-- that insert pushes one row from the page they are on onto the page they are
-- about to request, and they never see it.
--
-- @afterId = '' is how the first page asks for itself. A key is never the empty
-- string -- IScaffoldStore.CreateAsync says a store returning one sends the caller
-- to a page that does not exist -- so the empty string is free to mean "no cursor"
-- without colliding with a real position. The alternative, a separate first-page
-- query, doubles the file count for a branch PostgreSQL folds away anyway.
--
-- @pattern is an ILIKE pattern, not a search term, and COALESCE guards the one
-- nullable column: NULL ILIKE anything is NULL, not false, so an author-less row
-- would drop out of an unfiltered list without it. Both as List.sql had them.
SELECT
    id,
    title,
    author,
    body,
    published,
    view_count,
    created_at,
    slug
FROM articles
WHERE (title ILIKE @pattern
    OR COALESCE(author, '') ILIKE @pattern
    OR body ILIKE @pattern)
  AND (@afterId = '' OR id > @afterId)
ORDER BY id
LIMIT @take;
