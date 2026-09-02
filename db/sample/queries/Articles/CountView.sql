-- One more view (spec 022 R6), added in the database rather than read, added
-- and written back: two visits arriving together both count, because each is
-- one atomic UPDATE. Published only, so a request that will be answered 404
-- counts nothing. Rows affected is the answer: 0 means no such published row.
UPDATE articles
SET view_count = view_count + 1
WHERE slug = @slug
  AND published;
