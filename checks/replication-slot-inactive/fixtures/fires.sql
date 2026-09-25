-- threshold min_retained_wal = 0B
-- A physical slot that reserves WAL from the start and never gets a consumer.
SELECT pg_create_physical_replication_slot('fixture_slot', true);
CREATE TABLE fixture_wal AS SELECT g FROM generate_series(1, 1000) AS g;
