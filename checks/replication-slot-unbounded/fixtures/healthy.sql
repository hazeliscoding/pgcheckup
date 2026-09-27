-- server max_slot_wal_keep_size = 10GB
SELECT pg_create_physical_replication_slot('fixture_slot');
