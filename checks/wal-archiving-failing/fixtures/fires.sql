-- server archive_mode = on
-- server archive_command = false
-- `false` always fails. Switching WAL gives the archiver a segment to try, then this waits
-- until the archiver has reported its first failure.
CREATE TABLE fixture_wal AS SELECT g FROM generate_series(1, 1000) AS g;
SELECT pg_switch_wal();
DO $$
BEGIN
    FOR i IN 1..100 LOOP
        PERFORM pg_stat_clear_snapshot();
        EXIT WHEN (SELECT failed_count FROM pg_stat_archiver) > 0;
        PERFORM pg_sleep(0.1);
    END LOOP;
END
$$;
