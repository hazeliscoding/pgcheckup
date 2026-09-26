-- threshold min_duration = 0s
-- threshold min_idle = 0s
-- A transaction that has already committed holds nothing back.
BEGIN;
SELECT txid_current();
COMMIT;
