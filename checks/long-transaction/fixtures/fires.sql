-- threshold min_duration = 0s
-- threshold min_idle = 0s
-- The fixture's own session keeps this transaction open while the check runs.
BEGIN;
SELECT txid_current();
