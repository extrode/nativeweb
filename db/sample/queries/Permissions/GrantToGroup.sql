-- @params groupId:long, permission:string
INSERT INTO nw_group_grant (group_id, permission)
VALUES (@groupId, @permission)
ON CONFLICT (group_id, permission) DO NOTHING;
