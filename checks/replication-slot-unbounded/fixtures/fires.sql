-- Stock Postgres has no limit, and this slot makes it matter.
SELECT pg_create_physical_replication_slot('fixture_slot');
