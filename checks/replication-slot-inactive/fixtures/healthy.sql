-- threshold min_retained_wal = 0B
-- threshold min_xmin_age = 1
-- A slot that has never reserved WAL holds none back, and pins no xmin, so even the lowest
-- thresholds stay quiet.
SELECT pg_create_physical_replication_slot('fixture_slot');
CREATE TABLE fixture_wal AS SELECT g FROM generate_series(1, 1000) AS g;
