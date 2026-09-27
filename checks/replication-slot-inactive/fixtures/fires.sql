-- server wal_level = logical
-- threshold min_retained_wal = 1TB
-- threshold min_xmin_age = 1
-- A logical slot pins catalog_xmin from the moment it is created, and ages as transactions
-- go by. It keeps little WAL, so only the xmin half of the check fires. The WAL half is covered
-- by the scan tests, which fill a slot with more than 1 GB.
SELECT pg_create_logical_replication_slot('fixture_slot', 'pgoutput');
SELECT txid_current();
SELECT txid_current();
