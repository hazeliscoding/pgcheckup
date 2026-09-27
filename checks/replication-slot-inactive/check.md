---
id: replication-slot-inactive
title: Inactive replication slot
category: wal
severity: warning
min_version: 14
privileges: []
thresholds:
  min_retained_wal: 1GB
  min_xmin_age: 100000000
message: "Slot {subject} has been inactive[ for {inactive_for}][ and is holding {retained_wal:bytes} of WAL].[ Vacuum can't clean up after the last {xmin_age:count} transactions while it exists.]"
fix: |
  restart its consumer, or drop the slot:
  SELECT pg_drop_replication_slot({slot_literal});
---

## What breaks

A replication slot makes Postgres keep every WAL segment that its consumer hasn't confirmed. When the consumer stops, the slot keeps WAL for as long as it exists. Typical consumers are a replica that was removed, a paused CDC connector such as Debezium, or a subscription dropped without its slot. `pg_wal` grows until the disk is full, and then Postgres stops accepting writes.

A slot can also hold back vacuum while keeping little WAL. A logical slot pins `catalog_xmin`, and a physical slot used with `hot_standby_feedback` pins `xmin`. Vacuum can't clean up after any transaction since, which bloats tables and leads toward transaction ID wraparound. The check reports that from 100 million transactions (`min_xmin_age`).

## Fix

If the consumer should still exist, restart it and let it catch up. If it shouldn't, drop the slot:

```sql
SELECT pg_drop_replication_slot('slot_name');
```

Then cap the WAL any slot can keep with `max_slot_wal_keep_size`. A slot that passes the cap is invalidated instead of filling the disk. On Postgres 18, `idle_replication_slot_timeout` also invalidates slots that stay inactive for too long.

## Seen in

- [Postgres almost-outage postmortem: the hidden dangers of replication slots and autovacuum](https://dev.to/sasikumart/postgres-almost-outage-postmortem-the-hidden-dangers-of-replication-slots-and-autovacuum-2nem), DEV Community
- [The Insatiable Postgres Replication Slot](https://www.morling.dev/blog/insatiable-postgres-replication-slot/), Gunnar Morling: an inactive slot on an idle Amazon RDS database kept growing its WAL.
- [Replication slots](https://www.postgresql.org/docs/current/warm-standby.html#STREAMING-REPLICATION-SLOTS), PostgreSQL documentation: "replication slots can cause the server to retain so many WAL segments that they fill up the space allocated for pg_wal."
