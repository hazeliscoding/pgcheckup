SELECT 'max_slot_wal_keep_size' AS subject
WHERE current_setting('max_slot_wal_keep_size') = '-1'
  -- idle_replication_slot_timeout arrived in Postgres 18, and caps idle slots too.
  AND coalesce(current_setting('idle_replication_slot_timeout', true), '0') = '0'
  AND EXISTS (SELECT FROM pg_replication_slots)
