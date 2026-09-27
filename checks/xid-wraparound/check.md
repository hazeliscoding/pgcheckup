---
id: xid-wraparound
title: Transaction ID wraparound
category: ids
severity: warning
min_version: 14
privileges: []
thresholds:
  warning_age: 500000000
  critical_age: 1500000000
message: "[Database {database}][Table {table_name}][Temporary table {temp_table}] has used {xid_age:count} of its 2.1 billion transaction IDs."
fix: "[connect to {database} and scan it to find its oldest tables, then freeze them.][VACUUM (FREEZE, VERBOSE) {table_name};][only the session that created {temp_table} can vacuum it, so have it drop the table, or end that session.]"
---

## What breaks

Postgres numbers transactions with 32-bit IDs and reuses them in a circle. Vacuum marks old rows as frozen so that reuse is safe. If a table goes about 2.1 billion transactions without being frozen, Postgres stops accepting writes to protect the data, and the fix is a long single-user vacuum while the application is down.

Long before that, at 200 million by default (`autovacuum_freeze_max_age`), autovacuum starts an anti-wraparound vacuum that can't be cancelled, and schema changes queue behind its lock. From 1.6 billion, Postgres switches to its failsafe mode.

The usual cause is something that holds vacuum back: a long transaction, a forgotten prepared transaction or an inactive replication slot. The checks `long-transaction`, `prepared-transaction-orphaned` and `replication-slot-inactive` look for those.

This check measures tables in the database it connects to, and every other database in the cluster as a whole. It lists at most the 20 oldest tables, including temporary ones: only the session that created a temporary table can vacuum it, so a pooled connection that keeps one for months ages the whole database.

## Fix

Freeze the oldest tables first:

```sql
VACUUM (FREEZE, VERBOSE) public.orders;
```

If the age keeps climbing, find and end what holds vacuum back before vacuuming again. On a large table, the vacuum takes a while; let it finish rather than cancelling it.

## Seen in

- [Transaction ID wraparound in Postgres](https://blog.sentry.io/transaction-id-wraparound-in-postgres/), Sentry
- [What we learned from the recent Mandrill outage](https://mailchimp.com/what-we-learned-from-the-recent-mandrill-outage/), Mailchimp
- [Understanding an outage: concurrency control and vacuuming in PostgreSQL](https://duffel.com/blog/understanding-outage-concurrency-vacuum-postgresql), Duffel
- [Preventing transaction ID wraparound failures](https://www.postgresql.org/docs/current/routine-vacuuming.html#VACUUM-FOR-WRAPAROUND), PostgreSQL documentation
