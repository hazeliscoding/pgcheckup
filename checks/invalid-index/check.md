---
id: invalid-index
title: Invalid index
category: capacity
severity: warning
min_version: 14
privileges: [pg_read_all_stats]
message: "Index {subject} on {table_name} is invalid, usually left by a failed CREATE INDEX CONCURRENTLY. Queries never use it, but writes may still pay to maintain it."
fix: |
  rebuild it, or drop it if it isn't needed:
  REINDEX INDEX CONCURRENTLY {subject};  -- or DROP INDEX CONCURRENTLY {subject};
---

## What breaks

`CREATE INDEX CONCURRENTLY` builds an index without blocking writes, but if it fails (a duplicate value for a unique index, a deadlock, a cancelled migration), it leaves the index behind, marked invalid. The planner never uses it, so the queries it was built for keep scanning the table, which can overload the database once traffic grows. Meanwhile, writes may still keep it up to date, and an invalid unique index still rejects duplicates.

Migrations that retry often leave one invalid index per failed attempt.

The check ignores indexes still being built, and the parent index of a partitioned table, which stays invalid until every partition has its own.

## Fix

Rebuild it, or drop it if the migration that created it was abandoned:

```sql
REINDEX INDEX CONCURRENTLY public.accounts_email;
DROP INDEX CONCURRENTLY public.accounts_email;
```

If it was a unique index, remove the duplicate rows first, or the rebuild fails the same way.

## Seen in

- [Building indexes concurrently](https://www.postgresql.org/docs/current/sql-createindex.html#SQL-CREATEINDEX-CONCURRENTLY), PostgreSQL documentation
