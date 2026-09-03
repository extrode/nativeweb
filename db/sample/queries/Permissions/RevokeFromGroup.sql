-- @params groupId:long, permission:string
DELETE FROM nw_group_grant
WHERE group_id = @groupId
  AND permission = @permission;
