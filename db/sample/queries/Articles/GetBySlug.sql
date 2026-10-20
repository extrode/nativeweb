-- @first
-- The public article page (spec 022 R3). Published only, in the SQL rather than
-- after the read: an unpublished row and a missing one are the same null here,
-- so the page cannot tell them apart and neither can a visitor.
SELECT
    id,
    title,
    author,
    body,
    view_count,
    created_at,
    slug,
    status,
    published_at,
    category_key
FROM articles
WHERE slug = @slug
  AND status = 'published';
