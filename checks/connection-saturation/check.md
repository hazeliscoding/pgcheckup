---
id: connection-saturation
title: Connections running out
category: capacity
severity: warning
min_version: 14
privileges: [pg_read_all_stats]
thresholds:
  warning_ratio: 0.8
  critical_ratio: 0.95
message: "{used:count} of the {available:count} connections available to applications are in use. When they run out, Postgres refuses new connections."
fix: |
  see who holds them:
  SELECT usename, state, count(*) FROM pg_stat_activity GROUP BY 1, 2 ORDER BY 3 DESC;
  -- then close idle sessions, shrink the pools, or put a pooler such as PgBouncer in front.
---

## What breaks

Postgres accepts at most `max_connections` sessions, and keeps a few of them back for superusers (`superuser_reserved_connections`, and `reserved_connections` from Postgres 16). When applications have used the rest, every new connection fails with "too many clients already". Deploys, cron jobs and autoscaling are the usual moments: each new process opens its own pool.

Raising `max_connections` helps less than it seems, because each connection costs memory and the server slows down with too many active at once. A pooler such as PgBouncer lets many clients share a few connections.

## Fix

Find out which roles and states hold the connections:

```sql
SELECT usename, state, count(*) FROM pg_stat_activity GROUP BY 1, 2 ORDER BY 3 DESC;
```

Many `idle` sessions point to pools that are too large for the number of processes. Put a pooler in front, or shrink each pool.

## Seen in

- [Database outage: too many connections](https://gitlab.com/gitlab-com/gl-infra/production/-/issues/122), GitLab
- [PostgreSQL connections exhausted](https://gitlab.com/gitlab-org/omnibus-gitlab/-/issues/8292), GitLab
- [How to increase the max connections in Postgres](https://dba.stackexchange.com/questions/69438), DBA Stack Exchange
