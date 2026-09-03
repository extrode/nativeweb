-- @params subject:string, groupId:long
DELETE FROM nw_group_member
WHERE subject = @subject
  AND group_id = @groupId;
