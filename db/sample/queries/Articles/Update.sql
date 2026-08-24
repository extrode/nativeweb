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
UPDATE articles
SET title = @title,
    author = @author,
    body = @body,
    published = @published,
    view_count = @viewCount
WHERE id = @id;
