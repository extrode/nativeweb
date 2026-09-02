-- One published article's tags, by name, for its page (spec 022 R3). Keyed by
-- the article's slug, which is what the page has, and joined through articles
-- so that this is the page's read and not a per-row lookup.
SELECT
    t.slug,
    t.name
FROM articles a
JOIN article_tags at ON at.article_id = a.id
JOIN tags t ON t.slug = at.tag_slug
WHERE a.slug = @slug
  AND a.published
ORDER BY t.name, t.slug;
