-- The public tag listing (spec 022 R5): every tag, with how many published
-- articles carry it. LEFT JOINs so a tag with no articles is a row with 0, and
-- the published test is in the join condition so an unpublished article is
-- neither counted nor able to drop the tag.
SELECT
    t.slug,
    t.name,
    t.color,
    COUNT(a.id) AS published_count
FROM tags t
LEFT JOIN article_tags at ON at.tag_slug = t.slug
LEFT JOIN articles a ON a.id = at.article_id AND a.published
GROUP BY t.slug, t.name, t.color
ORDER BY t.name, t.slug;
