-- One row for the edit form. @first, so a missing key is a null rather than an
-- empty list, which is what IScaffoldStore<T>.GetAsync returns.
-- @first
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
WHERE id = @id;
