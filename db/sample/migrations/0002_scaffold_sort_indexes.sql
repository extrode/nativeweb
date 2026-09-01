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
--
-- THE NULLABLE COLUMN IS THE EXCEPTION, and it is not a small one. Measured with
-- EXPLAIN on 2026-09-01, the author queries get an "Index Scan using
-- articles_author_id_idx" with no sort node -- and the keyset boundary lands in
-- Filter, not Index Cond. The predicate has to read `author IS NULL OR (author, id)
-- > (...)` to keep author-less rows from vanishing, and a disjunction over IS NULL
-- is not a rangeable start point, so page N walks the index from the top and
-- discards N pages of rows. The ORDER is free; the POSITION is not. Sorting by a
-- non-null column stays O(page). Fixing it needs the null placement folded into the
-- index as an expression -- `((author IS NULL), author, id)` -- and both author
-- queries rewritten against it; noted in work/todo.md rather than done here.
--
-- The write side, since the read side is what an index migration always argues.
-- Each insert now writes four index entries instead of one, and title and author
-- being indexed means an edit to either can no longer take the HOT-update path.
-- And these are plain CREATE INDEX, so each takes a SHARE lock and blocks writes to
-- articles while it builds: CONCURRENTLY is unavailable because db/migrate.sh wraps
-- every file in a transaction. On a demo table that is instant. On a populated one
-- it is a write outage, and a consumer stamping this schema should build the
-- indexes out of band instead.

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
