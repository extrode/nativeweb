-- Every published article's address and creation time, for the sitemap (spec
-- 022 R8). Two columns rather than the row, because a sitemap of every article
-- is the one read here whose width matters. No LIMIT: a sitemap that stops at
-- twenty is a listing, not a sitemap; the 50,000-entry ceiling of one file is
-- noted in work/todo.md.
SELECT
    slug,
    created_at
FROM articles
WHERE status = 'published'
ORDER BY COALESCE(published_at, (created_at AT TIME ZONE 'UTC')::date) DESC, created_at DESC, id DESC;
