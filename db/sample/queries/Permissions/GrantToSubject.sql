-- @params subject:string, permission:string
INSERT INTO nw_subject_grant (subject, permission)
VALUES (@subject, @permission)
ON CONFLICT (subject, permission) DO NOTHING;
