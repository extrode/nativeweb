-- created_at is absent from the SET list because it is [ScaffoldReadOnly] and
-- nothing may change it after the insert.
--
-- There are two independent defences here, and it is worth saying which is which,
-- because each one hides the other when only one is broken. Both were measured.
--
--   1. The generator's emitted Build(form, existing) copies every read-only
--      property off the row already in the store, since the edit form renders no
--      input for one. Break just this -- emit `default!` instead -- and every test
--      over PostgreSQL still passes, because the column below is never written.
--   2. This SET list, which does not mention created_at. Break just this -- add
--      `created_at = NOW()` -- and the timestamp changes on every edit, generator
--      or no generator.
--
-- So adding created_at back to this statement is not harmless-because-redundant.
-- It removes the only defence that survives a change to the generator, and the
-- test that notices is Edit_FromTheBrowser_UpdatesTheRowAndLeavesCreatedAtAlone.
--
-- Rows affected is the return, which is what UpdateAsync's bool is derived from:
-- 0 means no such key.
-- @params title:string, author:string?, body:string, status:string, publishedAt:DateTime?, categoryKey:string?, slug:string?, id:string
--
-- slug is nullable on purpose (spec 022 R2): a blank slug on the edit form means
-- "keep the one the row has", and COALESCE is where that rule lives on this path.
-- The store validates a typed slug before it gets here; a duplicate is refused by
-- articles_slug_key, which the store turns into the form's message.
--
-- view_count is absent from the SET list since spec 023 (R8): the form no longer
-- carries it, so a visit counted while the edit form was open is kept. Only
-- CountView.sql writes it now. published_at arrives as a timestamp parameter
-- and is cast, because the generator maps DATE to DateTime and the store passes
-- the day at midnight; category_key is a key of categories or null, and an
-- unknown one is refused by the foreign key, which the store turns into the
-- form's message.
UPDATE articles
SET title = @title,
    author = @author,
    body = @body,
    status = @status,
    published_at = @publishedAt::date,
    category_key = @categoryKey,
    slug = COALESCE(@slug, slug)
WHERE id = @id;
