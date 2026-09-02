-- Spec 023: a status instead of a published flag, a publication date, and a
-- category an article may belong to.
--
-- Four things, each idempotent so db/migrate.sh can re-run the file:
--
--   1. categories -- a key and a name, the smallest table another row can refer
--      to. The two seeded on the in-memory path are inserted here with ON
--      CONFLICT DO NOTHING, as 0003 seeds the tags.
--   2. articles.status -- 'draft', 'published' or 'archived', text with a CHECK
--      rather than an enum type, because a CHECK can be changed in place and a
--      type cannot be reordered. Existing rows convert: published becomes
--      'published', unpublished becomes 'draft' (R3), and only then is the flag
--      dropped, so a re-run after a failure between the two steps still finds
--      what it needs. The store maps ArticleStatus to these values in lower
--      case; the app never writes anything the CHECK would refuse.
--   3. articles.published_at -- a DATE, nullable: the publication date is a
--      day, not an instant, and a row may not have one (R4).
--   4. articles.category_key -- nullable, ON DELETE SET NULL: deleting a
--      category leaves its articles with none (R5). The index on it is what
--      makes that cascade a lookup rather than a scan of articles.
--
-- The public order (R4) is the publication date, a row without one dated by its
-- creation day in UTC, then created_at, then id, newest first throughout. The
-- partial expression index at the end is that order over the published rows
-- only, in the directions the queries spell, so ListPublished.sql and its three
-- companions walk it rather than sort. `AT TIME ZONE 'UTC'` and not a bare
-- ::date cast: a cast from timestamptz depends on the session's TimeZone, which
-- PostgreSQL refuses to index, and a day that moved with the client's clock
-- would be the wrong day besides.

CREATE TABLE IF NOT EXISTS categories (
    key   TEXT PRIMARY KEY CHECK (key ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
    name  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS categories_name_key_idx
    ON categories (name, key);

INSERT INTO categories (key, name)
VALUES
    ('guides', 'Guides'),
    ('notes', 'Notes')
ON CONFLICT (key) DO NOTHING;

ALTER TABLE articles ADD COLUMN IF NOT EXISTS status TEXT;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'articles' AND column_name = 'published') THEN
        UPDATE articles
        SET status = CASE WHEN published THEN 'published' ELSE 'draft' END
        WHERE status IS NULL;
    END IF;
END
$$;

UPDATE articles SET status = 'draft' WHERE status IS NULL;

ALTER TABLE articles ALTER COLUMN status SET NOT NULL;
ALTER TABLE articles ALTER COLUMN status SET DEFAULT 'draft';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'articles_status_check') THEN
        ALTER TABLE articles
            ADD CONSTRAINT articles_status_check CHECK (status IN ('draft', 'published', 'archived'));
    END IF;
END
$$;

ALTER TABLE articles DROP COLUMN IF EXISTS published;

ALTER TABLE articles ADD COLUMN IF NOT EXISTS published_at DATE NULL;

ALTER TABLE articles ADD COLUMN IF NOT EXISTS category_key TEXT NULL
    REFERENCES categories (key) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS articles_category_key_idx
    ON articles (category_key);

CREATE INDEX IF NOT EXISTS articles_public_order_idx
    ON articles (
        (COALESCE(published_at, (created_at AT TIME ZONE 'UTC')::date)) DESC,
        created_at DESC,
        id DESC)
    WHERE status = 'published';
