-- @params groupId:long
-- One group's members, read through the group row. A point lookup on
-- nw_group_member.group_id alone reads as the child half of an N+1 pair to JNT8008;
-- going through the parent says what this is, one group's members, not one lookup
-- per row of the group list. Served by nw_group_member_group_id_idx, which exists
-- for this read: the composite primary key is subject-first and answers the
-- per-request direction instead.
SELECT m.subject
FROM nw_group g
JOIN nw_group_member m ON m.group_id = g.id
WHERE g.id = @groupId
ORDER BY m.subject;
