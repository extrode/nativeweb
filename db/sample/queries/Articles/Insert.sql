-- created_at is passed rather than left to the column default, because the
-- store stamps it: IScaffoldStore<T>.CreateAsync is handed a fully-formed
-- object, and having two places that can decide a row's creation time is how
-- they come to disagree. The default in the migration is a safety net for
-- hand-written INSERTs, not the path this app takes.
INSERT INTO articles (id, slug, title, author, body, published, view_count, created_at)
VALUES (@id, @slug, @title, @author, @body, @published, @viewCount, @createdAt);
