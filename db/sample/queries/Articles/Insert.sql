-- created_at is passed rather than left to the column default, because the
-- store stamps it: IScaffoldStore<T>.CreateAsync is handed a fully-formed
-- object, and having two places that can decide a row's creation time is how
-- they come to disagree. The default in the migration is a safety net for
-- hand-written INSERTs, not the path this app takes.
-- @params id:string, slug:string, title:string, author:string?, body:string, status:string, publishedAt:DateTime?, categoryKey:string?, viewCount:int, createdAt:DateTime
--
-- published_at is cast because the generator maps DATE to DateTime and Npgsql
-- sends a DateTime as a timestamp, which a DATE column will not take uncast.
INSERT INTO articles (id, slug, title, author, body, status, published_at, category_key, view_count, created_at)
VALUES (@id, @slug, @title, @author, @body, @status, @publishedAt::date, @categoryKey, @viewCount, @createdAt);
