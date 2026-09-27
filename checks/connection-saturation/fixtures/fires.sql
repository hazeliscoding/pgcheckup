-- server max_connections = 6
-- threshold warning_ratio = 0.6
-- Three slots are left for applications. This session and the check's make two, which only
-- reaches the threshold if the check can see this session.
SELECT 1;
