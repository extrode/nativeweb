-- @params groupId:long
-- RETURNING rather than a bare DELETE: JauntyQ maps a returning statement to a
-- list, so an empty result is the row that was not there, which is what the store
-- turns into PermissionConflictException. Without it a delete naming a group that
-- has gone since the page rendered succeeds silently and the admin redirects as
-- though it had done something.
--
-- No dependent table is named here. nw_group_member and nw_group_grant both
-- reference nw_group ON DELETE CASCADE (migration 0006), so the memberships and
-- the group's grants go with it.
DELETE FROM nw_group
WHERE id = @groupId
RETURNING id;
