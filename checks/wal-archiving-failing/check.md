---
id: wal-archiving-failing
title: WAL archiving failing
category: wal
severity: warning
min_version: 14
privileges: [pg_monitor]
skip_on: [rds, aurora, cloudsql, azure, supabase, neon]
thresholds:
  max_ready_age: 1h
message: "[archive_mode is {unset_mode}, but no archive_command is set, so WAL piles up in pg_wal and none of it is archived.][WAL archiving has been failing for {failing_for}, so point-in-time recovery has a gap and WAL piles up in pg_wal.][The oldest of {ready_count:count} WAL segments has waited {waiting_for} to be archived, so the archiver seems stuck.]"
fix: "read the server log for the archiver's errors. Set archive_command if it is empty, fix it or its destination if it fails or hangs (a full or unreachable destination is typical), or turn archive_mode off if you don't archive."
---

## What breaks

With `archive_mode` on, Postgres keeps each WAL segment until `archive_command` has copied it somewhere safe. If archiving fails or stops, two things go wrong:

- Point-in-time recovery has a gap from the last segment archived. A restore can't get past it.
- The segments that wait pile up in `pg_wal` until the disk is full, and Postgres stops accepting writes.

The check reports three cases: `archive_mode` on without a command, a command that keeps failing, and an archiver that makes no progress for an hour (`max_ready_age`) without logging a failure. It never prints `archive_command`, which can hold credentials.

Managed providers archive WAL themselves, so the check is skipped there.

## Fix

Read the server log for the command's error, and fix the command or its destination. When the command succeeds again, Postgres archives the backlog on its own. If you don't need archiving, turn `archive_mode` off, which needs a restart.

## Seen in

- [PostgreSQL archiver failure](https://vsevolod.net/postgresql-archiver-failure/), Vsevolod
- [Postgres is out of disk and how to recover: the dos and don'ts](https://www.crunchydata.com/blog/postgres-is-out-of-disk-and-how-to-recover-the-dos-and-donts), Crunchy Data
- [Setting up WAL archiving](https://www.postgresql.org/docs/current/continuous-archiving.html#BACKUP-ARCHIVING-WAL), PostgreSQL documentation
