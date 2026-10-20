-- @params subject:string, permission:string
-- Not validated against the catalog anywhere on this path: revoking is what an
-- operator does to a grant whose declaration is gone.
DELETE FROM nw_subject_grant
WHERE subject = @subject
  AND permission = @permission;
