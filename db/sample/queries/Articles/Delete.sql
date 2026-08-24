-- Rows affected distinguishes "deleted" from "no such key", which is exactly
-- the bool IScaffoldStore<T>.DeleteAsync returns.
DELETE FROM articles
WHERE id = @id;
