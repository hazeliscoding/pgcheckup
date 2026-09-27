---
id: multixact-wraparound
title: Multixact ID wraparound
category: ids
severity: warning
min_version: 14
privileges: []
thresholds:
  warning_age: 500000000
  critical_age: 1500000000
message: "[Database {database}][Table {table_name}][Temporary table {temp_table}] has used {mxid_age:count} of its 2.1 billion multixact IDs."
fix: "[connect to {database} and scan it to find its oldest tables, then freeze them.][VACUUM (FREEZE, VERBOSE) {table_name};][only the session that created {temp_table} can vacuum it, so have it drop the table, or end that session.]"
---

## What breaks

When more than one transaction locks the same row, as foreign keys and `SELECT … FOR SHARE` do, Postgres records the group as a multixact, with its own 32-bit ID. Like transaction IDs, multixact IDs wrap around. Vacuum has to freeze old ones, and if a table goes about 2.1 billion multixacts without that, Postgres stops accepting writes.

Autovacuum starts an anti-wraparound vacuum at 400 million by default (`autovacuum_multixact_freeze_max_age`). Workloads with many foreign keys, or many concurrent row locks, use multixacts fastest.

This check measures tables in the database it connects to, and every other database in the cluster as a whole. It lists at most the 20 oldest tables, including temporary ones: only the session that created a temporary table can vacuum it, so a pooled connection that keeps one for months ages the whole database.

## Fix

Freeze the oldest tables first:

```sql
VACUUM (FREEZE, VERBOSE) public.accounts;
```

As with transaction IDs, a long transaction, a forgotten prepared transaction or an inactive replication slot can hold vacuum back. pgcheckup's other checks look for them.

## Seen in

- [Root cause analysis: PostgreSQL MultiXact member exhaustion incidents](https://metronome.com/blog/root-cause-analysis-postgresql-multixact-member-exhaustion-incidents-may-2025), Metronome
- [Multixacts and wraparound](https://www.postgresql.org/docs/current/routine-vacuuming.html#VACUUM-FOR-MULTIXACT-WRAPAROUND), PostgreSQL documentation
