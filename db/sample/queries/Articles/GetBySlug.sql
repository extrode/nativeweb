-- @first
-- The public article page (spec 022 R3). Published only, in the SQL rather than
-- after the read: an unpublished row and a missing one are the same null here,
-- so the page cannot tell them apart and neither can a visitor.
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
WHERE slug = @slug
  AND published;
