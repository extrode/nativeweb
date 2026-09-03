-- @params subject:string, groupId:long
-- Idempotent, so adding a member twice is not an error the admin has to read.
INSERT INTO nw_group_member (subject, group_id)
VALUES (@subject, @groupId)
ON CONFLICT (subject, group_id) DO NOTHING;
