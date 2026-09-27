---
id: postgres-eol
title: Postgres version near or past end of life
category: capacity
severity: warning
min_version: 10
privileges: []
thresholds:
  warn_before: 90d
message: "PostgreSQL {subject}[ reached end of life on {ended_on} and gets no more bug or security fixes][ reaches end of life on {ends_on}, in {time_left}]."
fix: plan an upgrade to PostgreSQL {latest} with pg_upgrade or logical replication, and rehearse it on a copy first.
---

## What breaks

Each major version of Postgres gets fixes for five years. After its end-of-life date, security holes and data-corruption bugs found in it stay open, and extensions and managed providers drop it. Managed providers also force an upgrade on their own schedule, or charge for extended support.

The check warns once a version is past its end of life, and gives notice (`info`) from 90 days before (`warn_before`). The dates ship inside each pgcheckup release, and the check compares them with the server's clock.

## Fix

Upgrade to a supported major version. `pg_upgrade` is fastest on a self-managed server; logical replication keeps downtime shortest. Either way, rehearse on a copy first: extensions and query plans can change between versions.

## Seen in

- [How we upgraded our 4 TB main application Postgres database](https://retool.com/blog/how-we-upgraded-postgresql-database), Retool
- [Versioning policy](https://www.postgresql.org/support/versioning/), PostgreSQL
