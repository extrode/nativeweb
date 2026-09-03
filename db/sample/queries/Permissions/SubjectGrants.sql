-- @params subject:string
-- One subject's direct grants, without the group ones: this is the access admin's
-- screen, where a grant is listed so it can be revoked, and only a direct grant is
-- revocable from there. Through the subject row for the reason in Members.sql.
SELECT sg.permission
FROM nw_subject s
JOIN nw_subject_grant sg ON sg.subject = s.subject
WHERE s.subject = @subject
ORDER BY sg.permission;
