---
id: autovacuum-disabled
title: Autovacuum turned off
category: cleanup
severity: warning
min_version: 14
privileges: []
message: "[{setting} is off, so dead rows pile up in every table, and tables are only frozen when wraparound forces it.][Table {table_name} has autovacuum turned off, so its dead rows pile up, and it is only frozen when wraparound forces it.]"
fix: "[ALTER SYSTEM SET {setting} = on;\nSELECT pg_reload_conf();][ALTER TABLE {table_name} RESET (autovacuum_enabled, toast.autovacuum_enabled);]"
---

## What breaks

Autovacuum removes the dead rows that updates and deletes leave behind, keeps planner statistics current, and freezes old rows before transaction IDs wrap around. With it off, tables and indexes bloat, queries slow down as statistics go stale, and freezing only happens in the emergency anti-wraparound vacuum, which can't be cancelled and blocks schema changes.

Autovacuum needs `track_counts` to know which tables changed, so turning that off stops it too. A single table can also have it turned off with `autovacuum_enabled = false`, often left over from a bulk load.

The check looks at the server's settings, and at the tables of the database it connects to.

## Fix

Turn it back on for the server:

```sql
ALTER SYSTEM SET autovacuum = on;
SELECT pg_reload_conf();
```

Or for a table:

```sql
ALTER TABLE public.events RESET (autovacuum_enabled, toast.autovacuum_enabled);
```

If a table was off for long, run `VACUUM (ANALYZE, VERBOSE)` on it once.

## Seen in

- [A production story: downtime caused by Postgres transaction ID wraparound](https://www.sqlservercentral.com/articles/i-too-have-a-production-story-a-downtime-caused-by-postgres-transaction-id-wraparound-problem), SQLServerCentral
- [autovacuum_enabled storage parameter](https://www.postgresql.org/docs/current/sql-createtable.html#RELOPTION-AUTOVACUUM-ENABLED), PostgreSQL documentation
