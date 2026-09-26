---
id: idle-transaction-timeout
title: No timeout for idle transactions
category: cleanup
severity: info
min_version: 14
privileges: []
message: "{subject} is not set, so a session left idle in a transaction holds back vacuum until someone ends it."
fix: |
  set it for the whole server, or only for your application's role:
  ALTER SYSTEM SET idle_in_transaction_session_timeout = '10min';
  SELECT pg_reload_conf();
---

## What breaks

Nothing yet. But without `idle_in_transaction_session_timeout`, a session that opens a transaction and then waits, because of an application bug, a crashed worker or a console left open, keeps it open indefinitely. While it does, vacuum can't clean up after any transaction since, and its locks stay held. `long-transaction` reports such sessions when they happen; this timeout ends them on its own.

Postgres ships with the timeout off, so most databases get this finding. That is why it is `info`.

The check passes when the timeout, or `transaction_timeout` on Postgres 17 and later, is set for the server, a database or a role.

## Fix

Pick a limit longer than any transaction your application means to leave idle, and set it for the server or for the application's role:

```sql
ALTER SYSTEM SET idle_in_transaction_session_timeout = '10min';
SELECT pg_reload_conf();

ALTER ROLE app SET idle_in_transaction_session_timeout = '10min';
```

On a managed provider, set it in the parameter group or its equivalent.

## Seen in

- [A production story: downtime caused by Postgres transaction ID wraparound](https://www.sqlservercentral.com/articles/i-too-have-a-production-story-a-downtime-caused-by-postgres-transaction-id-wraparound-problem), SQLServerCentral
- [idle_in_transaction_session_timeout](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-IDLE-IN-TRANSACTION-SESSION-TIMEOUT), PostgreSQL documentation
