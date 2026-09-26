---
id: prepared-transaction-orphaned
title: Orphaned prepared transaction
category: cleanup
severity: warning
min_version: 14
privileges: []
thresholds:
  min_age: 5min
message: "Prepared transaction {subject} in {database} has waited {waiting_for} for a COMMIT PREPARED or ROLLBACK PREPARED, holding its locks and holding back vacuum."
fix: |
  if the coordinator that prepared it is gone, finish it by hand in {database}:
  COMMIT PREPARED {gid_literal};  -- or ROLLBACK PREPARED {gid_literal};
---

## What breaks

A prepared transaction is the first half of a two-phase commit. It survives disconnects and restarts until someone commits or rolls it back. If the coordinator that prepared it crashes or forgets it, the transaction stays forever: its row locks keep blocking writes, and vacuum can't clean up after it, which leads toward transaction ID wraparound.

It doesn't show up in `pg_stat_activity`, because no session holds it, so it is easy to miss.

Two-phase commit is off unless `max_prepared_transactions` is above zero, so on most servers this check finds nothing.

## Fix

Find out from the coordinator (a transaction manager, a message queue, an application server) whether the transaction should commit or roll back, then finish it in its database:

```sql
COMMIT PREPARED 'fixture';
ROLLBACK PREPARED 'fixture';
```

## Seen in

- [PREPARE TRANSACTION](https://www.postgresql.org/docs/current/sql-prepare-transaction.html), PostgreSQL documentation
