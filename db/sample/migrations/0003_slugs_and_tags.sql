-- Spec 022: a public address for every article, and tags that persist.
--
-- Three things, each idempotent so db/migrate.sh can re-run the file:
--
--   1. articles.slug -- the article's public address, unique, NOT NULL, held to
--      the alphabet apps/sample/Slugs.cs defines. Existing rows are backfilled
--      from their titles in one set-based UPDATE, not a loop: the test database
--      accumulates rows across runs (db/sample/README.md) and had ~46k when this
--      was written.
--   2. tags -- until now an in-memory list even on the database path, so an admin
--      edit to a tag did not survive a restart. The three seeded tags are inserted
--      here with ON CONFLICT DO NOTHING, which is what "seeded" means on this path;
--      articles are not seeded here, as they never were.
--   3. article_tags -- the relation. ON DELETE CASCADE both ways: deleting an
--      article or a tag removes its links, and nothing else refers to a link.
--
-- The backfill derives the way Slugs.Derive does: lowercase, every run of
-- characters outside [a-z0-9] collapsed to one hyphen, the ends trimmed, capped at
-- 80 characters, and `article` when nothing usable is left. Where two rows derive
-- the same base, every row but the earliest (by created_at, then id) has `-<id>`
-- appended. The id is unique, so the result is unique by construction -- with one
-- theoretical exception, a title that already reads like another row's suffixed
-- slug, which the unique index makes loud rather than silent.
--
-- The CHECK constraint is what keeps the column honest against a hand-written
-- INSERT; the stores refuse the same values before they get here, with a message
-- the admin form shows. A DO block guards the ADD CONSTRAINT because there is no
-- IF NOT EXISTS for one.

ALTER TABLE articles ADD COLUMN IF NOT EXISTS slug TEXT;

WITH derived AS (
    SELECT
        id,
        COALESCE(
            NULLIF(
                trim(BOTH '-' FROM left(
                    trim(BOTH '-' FROM regexp_replace(lower(title), '[^a-z0-9]+', '-', 'g')),
                    80)),
                ''),
            'article') AS base,
        created_at
    FROM articles
    WHERE slug IS NULL
),
numbered AS (
    SELECT
        id,
        base,
        row_number() OVER (PARTITION BY base ORDER BY created_at, id) AS rn
    FROM derived
)
UPDATE articles a
SET slug = CASE WHEN n.rn = 1 THEN n.base ELSE n.base || '-' || a.id END
FROM numbered n
WHERE n.id = a.id;

ALTER TABLE articles ALTER COLUMN slug SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'articles_slug_check') THEN
        ALTER TABLE articles
            ADD CONSTRAINT articles_slug_check CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$');
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS articles_slug_key
    ON articles (slug);

CREATE TABLE IF NOT EXISTS tags (
    slug   TEXT PRIMARY KEY CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
    name   TEXT NOT NULL,
    color  TEXT     NULL
);

-- The scaffolded tag list sorts by name with slug as the tiebreak (spec 017's
-- keyset rule); the index matches the ORDER BY the same way 0002's do.
CREATE INDEX IF NOT EXISTS tags_name_slug_idx
    ON tags (name, slug);

CREATE TABLE IF NOT EXISTS article_tags (
    article_id  TEXT NOT NULL REFERENCES articles (id) ON DELETE CASCADE,
    tag_slug    TEXT NOT NULL REFERENCES tags (slug) ON DELETE CASCADE,
    PRIMARY KEY (article_id, tag_slug)
);

-- The tag page reads the relation from the tag side; the primary key serves the
-- article side.
CREATE INDEX IF NOT EXISTS article_tags_tag_slug_article_id_idx
    ON article_tags (tag_slug, article_id);

INSERT INTO tags (slug, name, color)
VALUES
    ('aot', 'NativeAOT', '#5bbd83'),
    ('htmx', 'HTMX', '#7aa2f7'),
    ('postgres', 'PostgreSQL', '#336791')
ON CONFLICT (slug) DO NOTHING;
