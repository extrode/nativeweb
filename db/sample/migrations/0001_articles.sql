-- The scaffolding demo's own schema, deliberately NOT part of db/migrations.
--
-- db/migrations/ is epass.io's identity schema and db/schema/jaunty.schema.json
-- is its committed snapshot; a demo `articles` table added there would ship to
-- production and appear in every consumer's generated data layer. The demo owns
-- its own database, its own migration and its own snapshot instead.
--
-- Columns mirror apps/sample/Models.cs exactly, because the point of this table
-- is to show a JauntyQ store satisfying IScaffoldStore<Article> with no mapping
-- layer between the two.

CREATE TABLE IF NOT EXISTS articles (
    -- text, not bigint identity: IScaffoldStore<T> keys on string, and the demo
    -- generates an 8-char key rather than letting the database allocate one. A
    -- real consumer with an identity key returns id::text from CreateAsync.
    id          TEXT        PRIMARY KEY,
    title       TEXT        NOT NULL,
    author      TEXT            NULL,
    body        TEXT        NOT NULL,
    published   BOOLEAN     NOT NULL DEFAULT FALSE,
    view_count  INTEGER     NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- The scaffolded list page orders by key and pages with LIMIT/OFFSET, and the
-- search predicate is an unanchored ILIKE across three columns. Neither is
-- servable from an index, which is honest for a demo of a few rows and would be
-- a trigram index in anything real. Said here so nobody copies the omission
-- believing it was considered and found unnecessary.
