-- threshold min_retained_wal = 0B
-- A slot that has never reserved WAL holds none back, so even a zero threshold stays quiet.
SELECT pg_create_physical_replication_slot('fixture_slot');
CREATE TABLE fixture_wal AS SELECT g FROM generate_series(1, 1000) AS g;
