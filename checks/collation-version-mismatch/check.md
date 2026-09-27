---
id: collation-version-mismatch
title: Collation changed under the database
category: capacity
severity: critical
min_version: 15
privileges: []
message: "[Database {database}][Collation {collation_name}] was recorded with version {recorded}, but the system now provides {actual}. Text indexes that use it can return wrong results or let duplicate keys in."
fix: "[rebuild its indexes while connected to it, then record the new version:\nREINDEX DATABASE {database_ident};\nALTER DATABASE {database_ident} REFRESH COLLATION VERSION;][rebuild the indexes that use it, then record the new version:\nALTER COLLATION {collation_name} REFRESH VERSION;]"
---

## What breaks

Text indexes are sorted by a collation, which comes from the operating system's C library (glibc) or from ICU. When an OS upgrade changes how that library sorts, as glibc 2.28 did for many languages, the order stored in existing indexes no longer matches. Lookups miss rows that are there, and unique indexes let duplicates in. Nothing reports an error; the data just goes quietly wrong.

Postgres records each collation's version when it is created, and from Postgres 15 the version of each database's default collation too. This check compares those with what the system provides now. It looks at every database's default collation, and at collations that indexes use in the database it connects to.

When the system can't report a version (the C collation, or a C library that doesn't version its collations), there is nothing to compare, and the check stays quiet.

## Fix

Rebuild the affected indexes, then record the new version so Postgres stops warning:

```sql
REINDEX DATABASE app;
ALTER DATABASE app REFRESH COLLATION VERSION;
```

`REINDEX DATABASE` rebuilds every index and takes locks as it goes. On a large database, rebuild only the text indexes, with `REINDEX INDEX CONCURRENTLY`. A unique index that now holds duplicates fails to rebuild until you remove them.

## Seen in

- [Unique constraint violation in Postgres due to an OS upgrade](https://support.atlassian.com/bitbucket-data-center/kb/unique-constraint-violation-in-postgres-due-to-os-upgrade/), Atlassian
- [Upgrading the operating system for PostgreSQL](https://docs.gitlab.com/administration/postgresql/upgrading_os/), GitLab
- [glibc collations and data corruption](https://www.crunchydata.com/blog/glibc-collations-and-data-corruption), Crunchy Data
