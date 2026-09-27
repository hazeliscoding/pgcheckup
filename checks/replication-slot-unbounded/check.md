---
id: replication-slot-unbounded
title: Replication slots without a WAL limit
category: wal
severity: warning
min_version: 14
privileges: []
message: "{subject} is -1 and this server has replication slots, so one stuck slot can keep WAL until the disk fills."
fix: |
  cap it below the free space on the WAL disk:
  ALTER SYSTEM SET max_slot_wal_keep_size = '50GB';
  SELECT pg_reload_conf();
---

## What breaks

A replication slot keeps every WAL segment its consumer hasn't confirmed. With `max_slot_wal_keep_size = -1`, the default, there is no limit: one consumer that stops, a CDC connector or a replica, makes `pg_wal` grow until the disk is full and Postgres stops accepting writes. `replication-slot-inactive` reports such a slot once it has stopped; this limit keeps it from taking the server down.

With a limit, a slot that falls too far behind is invalidated instead. Its consumer then has to resynchronize, which is much cheaper than an outage.

The check only fires when the server has at least one slot, because without slots the setting doesn't matter. On Postgres 18, `idle_replication_slot_timeout` also counts as a limit.

## Fix

Choose a size below the free space on the disk that holds `pg_wal`, with room to spare:

```sql
ALTER SYSTEM SET max_slot_wal_keep_size = '50GB';
SELECT pg_reload_conf();
```

On a managed provider, set it in the parameter group or its equivalent.

## Seen in

- [Postgres almost-outage postmortem: the hidden dangers of replication slots and autovacuum](https://dev.to/sasikumart/postgres-almost-outage-postmortem-the-hidden-dangers-of-replication-slots-and-autovacuum-2nem), DEV Community
- [max_slot_wal_keep_size](https://www.postgresql.org/docs/current/runtime-config-replication.html#GUC-MAX-SLOT-WAL-KEEP-SIZE), PostgreSQL documentation
