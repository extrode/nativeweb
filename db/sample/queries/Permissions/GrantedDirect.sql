-- @params subject:string
-- One subject's own grants. Half of what a request needs; GrantedThroughGroups.sql
-- is the other half.
--
-- One query would have been better and is not available: JauntyQ refuses set
-- operations (JNT1006) because it models only the first branch's column shape, and
-- the join-shaped alternative -- direct grants LEFT JOINed against group grants --
-- is a cross product of the two sets for no gain. Two reads, merged in the store,
-- and the held set is memoised per request either way.
SELECT sg.permission
FROM nw_subject s
JOIN nw_subject_grant sg ON sg.subject = s.subject
WHERE s.subject = @subject;
