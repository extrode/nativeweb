-- @params articleId:string
-- The tags one article holds, as the admin edit form reads them (spec 023 R1):
-- slugs only, in slug order, so the checkbox list and the store agree on the
-- set. Read once per form, by the article's key, through the article row: a
-- point lookup on the link table's foreign key alone reads as the child half of
-- an N+1 pair to JNT8008, and going through the parent row says what this is,
-- one article's links, not one lookup per row of a list.
SELECT at.tag_slug
FROM articles a
JOIN article_tags at ON at.article_id = a.id
WHERE a.id = @articleId
ORDER BY at.tag_slug;
