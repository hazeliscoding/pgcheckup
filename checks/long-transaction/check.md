---
id: long-transaction
title: Long or idle transaction
category: cleanup
severity: warning
min_version: 14
privileges: [pg_read_all_stats]
thresholds:
  min_duration: 1h
  min_idle: 10min
message: "Session {subject}[ ({role_name} on {database})] has had a transaction open for {open_for}[ and has been idle in it for {idle_for}]."
fix: |
  if the session is stuck or abandoned, end it:
  SELECT pg_terminate_backend({subject});
---

## What breaks

While a transaction is open, vacuum can't remove any row that the transaction might still see, anywhere in the database. Tables and indexes bloat, queries slow down, and transaction IDs can't be frozen, which leads toward wraparound. The transaction's locks also stay held, and a schema change that queues behind them blocks the traffic behind it.

A session that is idle inside a transaction is the usual culprit: an application that began a transaction and never committed, or a console left open. It does nothing, but holds everything back.

Backups with `pg_dump` also keep a transaction open for their whole run. That is expected, but it has the same effect on a busy database.

## Fix

Find out what the session is and whether it is still needed. If it is stuck or abandoned, end it:

```sql
SELECT pg_terminate_backend(4127);
```

To stop sessions from idling in a transaction for good, set `idle_in_transaction_session_timeout` (see `idle-transaction-timeout`).

## Seen in

- [Post-mortem: service disruption on January 21–22, 2020](https://www.figma.com/blog/post-mortem-service-disruption-on-january-21-22-2020/), Figma
- [How we upgraded our 4 TB main application Postgres database](https://retool.com/blog/how-we-upgraded-postgresql-database), Retool
- [Zero-downtime Postgres migrations: the hard parts](https://gocardless.com/blog/zero-downtime-postgres-migrations-the-hard-parts/), GoCardless
