-- @params articleId:string
-- Half of "make these the article's tags, no more and no fewer": the store
-- deletes and re-inserts inside one transaction rather than diffing, so the
-- row's side of the relation is exactly what the form posted.
DELETE FROM article_tags
WHERE article_id = @articleId;
