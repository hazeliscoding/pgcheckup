---
id: writes-data
title: A check that writes
category: capacity
severity: warning
min_version: 14
privileges: []
message: Wrote row {subject}.
fix: nothing
---

## What breaks

Nothing. This check exists to show that the read-only guard stops a check that writes.

## Fix

Delete this check.

## Seen in

- [SET TRANSACTION](https://www.postgresql.org/docs/current/sql-set-transaction.html), PostgreSQL documentation
