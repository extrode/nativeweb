-- @params groupId:long
-- One group's grants, through the group row for the reason in Members.sql.
SELECT gg.permission
FROM nw_group g
JOIN nw_group_grant gg ON gg.group_id = g.id
WHERE g.id = @groupId
ORDER BY gg.permission;
