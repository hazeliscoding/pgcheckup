SELECT s.slot_name AS subject,
       quote_literal(s.slot_name) AS slot_literal,
       -- inactive_since arrived in Postgres 17. Reading it through to_jsonb keeps one query
       -- for every supported version, and gives NULL before 17.
       now() - (to_jsonb(s) ->> 'inactive_since')::timestamptz AS inactive_for,
       w.retained_wal
FROM pg_replication_slots AS s
CROSS JOIN LATERAL (
    -- On a standby, pg_current_wal_lsn() raises an error; the replay position is its equivalent.
    SELECT pg_wal_lsn_diff(
               CASE WHEN pg_is_in_recovery() THEN pg_last_wal_replay_lsn() ELSE pg_current_wal_lsn() END,
               s.restart_lsn)::bigint AS retained_wal
) AS w
WHERE NOT s.active
  -- A lost slot has already been invalidated and holds no WAL.
  AND s.wal_status IS DISTINCT FROM 'lost'
  -- On a standby, a slot synced from the primary (Postgres 17 and later) always looks inactive,
  -- and can't be dropped there. Its consumer is on the primary, where this check covers it.
  AND NOT coalesce((to_jsonb(s) ->> 'synced')::boolean, false)
  AND w.retained_wal >= @min_retained_wal
ORDER BY w.retained_wal DESC
