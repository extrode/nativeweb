-- @params name:string
-- The identity column supplies the key, so the new id comes back rather than
-- being minted here. A duplicate name raises 23505 on the UNIQUE constraint,
-- which the store turns into PermissionConflictException.
INSERT INTO nw_group (name)
VALUES (@name)
RETURNING id;
