-- @params subject:string
-- The same shape as DeleteGroup: an empty result is a subject the store has not
-- seen, and the cascades on nw_group_member and nw_subject_grant remove the
-- memberships and the direct grants. The groups themselves are untouched.
DELETE FROM nw_subject
WHERE subject = @subject
RETURNING subject;
