-- @params subject:string
-- What one subject holds by being in a group. Routed through nw_group rather than
-- joining nw_group_member straight to nw_group_grant on group_id: that join matches
-- no declared foreign key and JNT8006 refuses it, which is the check earning its
-- keep -- both columns point at nw_group.id, and the schema says so through the
-- parent row.
SELECT gg.permission
FROM nw_subject s
JOIN nw_group_member m ON m.subject = s.subject
JOIN nw_group g ON g.id = m.group_id
JOIN nw_group_grant gg ON gg.group_id = g.id
WHERE s.subject = @subject;
