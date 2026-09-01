-- Indexes for the scaffolded list's keyset paging, one per sortable column.
--
-- 0001 says the list "orders by key and pages with LIMIT/OFFSET", and that both it
-- and the ILIKE search are unservable from an index. Half of that is now out of
-- date: spec 017 replaced offset paging with keyset, and a keyset page IS an index
-- range scan -- ORDER BY <col>, id with a LIMIT walks the index and stops, with no
-- sort node and no rows read past the page.
--
-- Each index is the composite (column, id), not the column alone, because the
-- query orders by both: the key is the tiebreaker that gives a page boundary
-- inside a run of equal values one answer instead of two. An index on the column
-- alone leaves PostgreSQL sorting each page to resolve the ties.
--
-- What has NOT changed is the search. The predicate is an unanchored ILIKE across
-- three columns, no index serves it, and a filtered list still scans the table --
-- these indexes earn their place on the unfiltered list, which is the common one.
-- A real consumer adds a trigram index for the other case; 0001 said so and it is
-- still true. Said again here so nobody reads "we added indexes" as "paging is
-- indexed now" for every query shape.
--
-- No index for view_count or published: neither carries [ScaffoldSort], so no
-- query orders by them. An index nothing orders by is write cost for nothing.

CREATE INDEX IF NOT EXISTS articles_title_id_idx
    ON articles (title, id);

-- NULLS LAST matched to ListByAuthorAsc.sql. It is the default for ASC, and the
-- index has to state it for the same reason the query does: an index whose null
-- placement disagrees with the ORDER BY cannot serve it, and the failure is a
-- silent sort node rather than an error.
CREATE INDEX IF NOT EXISTS articles_author_id_idx
    ON articles (author NULLS LAST, id);

CREATE INDEX IF NOT EXISTS articles_created_at_id_idx
    ON articles (created_at, id);
