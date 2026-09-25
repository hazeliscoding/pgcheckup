# pgcheckup

**Find the Postgres problems that take production down, before they do.** pgcheckup is a read-only CLI that checks a database for the failures that turn into outages, explains each one in plain words, and gives you the SQL to fix it.

In February 2019, one of the Postgres shards behind Mailchimp's Mandrill [ran out of transaction IDs](https://mailchimp.com/what-we-learned-from-the-recent-mandrill-outage/). Postgres stopped accepting writes to protect the data, and Mandrill took almost two days to recover. Three months earlier, the team had noticed transaction IDs climbing to half the limit at peak load.

Most of these failures show up in the system catalogs weeks ahead: a table's transaction ID age, a replication slot nobody reads, WAL archiving that failed last night. Teams without a DBA rarely look. pgcheckup looks for them and tells you what to do.

> **Status:** planning. There is nothing to install yet. See [ROADMAP.md](ROADMAP.md).

## How it works

```sh
# Any role with pg_monitor will do. The password comes from PGPASSWORD or ~/.pgpass.
pgcheckup scan "postgres://checkup@db.example.com:5432/app?sslmode=verify-full"
```

```text
pgcheckup · app on db.example.com · PostgreSQL 17.6 · Amazon RDS

CRITICAL  xid-wraparound
          Table orders has used 1.61 billion of its 2.1 billion transaction IDs.
          Vacuum can't freeze it while pid 4127 holds a transaction open (6 days).
          Fix: end pid 4127, then run VACUUM (FREEZE) orders;

WARNING   replication-slot-inactive
          Slot debezium has been inactive for 3 days and is holding 48 GB of WAL.
          Fix: restart its consumer, or drop the slot:
               SELECT pg_drop_replication_slot('debezium');

11 passed · 1 critical · 1 warning · 1 skipped (wal-archiving-failing: managed by Amazon RDS)
```

## What it checks

- **Running out of IDs:** transaction ID and multixact wraparound, and `int4` sequences and identity columns near their limit.
- **Cleanup that can't run:** long transactions, sessions idle inside a transaction, forgotten prepared transactions, and tables with autovacuum turned off.
- **Disks filling with WAL:** inactive replication slots, slots with no WAL limit, and failing WAL archiving.
- **Capacity and settings:** connection saturation, `fsync`, `full_page_writes` or `autovacuum` turned off, invalid indexes, and Postgres versions past end of life.

Every finding says what breaks and how to fix it. `pgcheckup explain <check>` prints the full note, with links to incidents where it happened.

## Safe to run on production

- **Read-only.** Every check runs inside a read-only transaction with statement and lock timeouts. pgcheckup prints fixes. It never runs them.
- **Least privilege.** The built-in `pg_monitor` role is enough. `pgcheckup grant` prints the SQL for a checkup role, and you decide whether to run it.
- **Aware of managed Postgres.** RDS, Aurora, Cloud SQL, Azure, Supabase and Neon hide some views. Checks that can't run there are reported as skipped, with the reason.
- **No network beyond your database.** No telemetry and no update check. Reports never include query text, row data or your password.
- **Open source**, so you can check all of this.

## In CI

`pgcheckup scan` exits 0 when no finding reaches `--fail-on` (default `critical`), 1 when one does, and 2 when the scan couldn't run. `--format json` and `--format markdown` are there for pipelines and pull requests. A baseline, so CI fails only on new findings, comes with v0.2.

## Prior art

pgcheckup is narrow on purpose. If you want broader coverage, these are good:

- [pg-healthcheck](https://www.pgedge.com/blog/introducing-pg-healthcheck-postgresql-health-diagnostics) from pgEdge runs 180+ checks and is built for DBAs.
- [pgdoctor](https://github.com/emancu/pgdoctor) is an opinionated check-up in Go.
- [postgres-checkup](https://postgres.ai/docs/checkup/) from PostgresAI does deep analysis of heavily loaded clusters. Its successor is `postgresai checkup`. The names are close, but the projects are unrelated.
- [splinter](https://github.com/supabase/splinter) is Supabase's linter for performance and security.
- [pganalyze](https://pganalyze.com) is commercial, continuous monitoring with index advice.

pgcheckup runs fewer checks. Each one maps to a failure that causes outages and is written for people who aren't DBAs.

## Contributing

Each check is one folder: a read-only query, a Markdown note that explains it, and two fixtures, one that triggers it and one that doesn't. If a Postgres failure has bitten you, adding its check is a good first contribution. The check guide arrives with v0.2.

## License

[Apache-2.0](LICENSE)
