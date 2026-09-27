---
id: dangerous-settings
title: Settings that risk data
category: capacity
severity: critical
min_version: 14
privileges: []
message: "{subject} is {setting}. Postgres then can't protect your data from a crash or a damaged page."
fix: |
  turn it back:
  ALTER SYSTEM SET {subject} = {safe_value};
  SELECT pg_reload_conf();
---

## What breaks

Three settings trade Postgres's protection of your data for speed or convenience:

- `fsync = off`: Postgres doesn't wait for writes to reach the disk. After a crash or power loss, the database can be corrupt, not just missing recent commits.
- `full_page_writes = off`: after a crash, a page that was half written can't be repaired from WAL, which corrupts it.
- `zero_damaged_pages = on`: Postgres silently replaces damaged pages with empty ones, which destroys the rows in them. It is meant for a one-off rescue, never for normal running.

They are sometimes turned off to speed up a bulk load or a test server, and left that way.

## Fix

Turn the setting back and reload:

```sql
ALTER SYSTEM SET fsync = on;
SELECT pg_reload_conf();
```

If the server ran with `fsync` or `full_page_writes` off through a crash, check it for corruption, for example with `pg_amcheck`.

## Seen in

- [fsync](https://www.postgresql.org/docs/current/runtime-config-wal.html#GUC-FSYNC), PostgreSQL documentation
- [full_page_writes](https://www.postgresql.org/docs/current/runtime-config-wal.html#GUC-FULL-PAGE-WRITES), PostgreSQL documentation
- [zero_damaged_pages](https://www.postgresql.org/docs/current/runtime-config-developer.html#GUC-ZERO-DAMAGED-PAGES), PostgreSQL documentation
