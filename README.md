<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/lockup-dark.svg">
    <img alt="pgcheckup" src="docs/brand/lockup.svg" height="40">
  </picture>
</h1>

**Find the Postgres problems that take production down, before they do.** pgcheckup is a read-only CLI that checks a database for the failures that turn into outages, explains each one in plain words, and gives you the SQL to fix it.

In February 2019, one of the Postgres shards behind Mailchimp's Mandrill [ran out of transaction IDs](https://mailchimp.com/what-we-learned-from-the-recent-mandrill-outage/). Postgres stopped accepting writes to protect the data, and Mandrill took almost two days to recover. Three months earlier, the team had noticed transaction IDs climbing to half the limit at peak load.

Most of these failures show up in the system catalogs weeks ahead: a table's transaction ID age, a replication slot nobody reads, WAL archiving that failed last night. Teams without a DBA rarely look. pgcheckup looks for them and tells you what to do.

> **Status:** early development. The 15 checks of v0.1 run end to end, but there is no release to install yet. See [ROADMAP.md](ROADMAP.md).

![pgcheckup scanning a database: an inactive replication slot holding 1.07 GB of WAL, no WAL limit for slots, and no timeout for idle transactions](docs/scan.gif)

## How it works

```sh
# Any role with pg_monitor will do. The password comes from PGPASSWORD or ~/.pgpass.
pgcheckup scan "postgres://checkup@db.example.com:5432/app?sslmode=verify-full"
```

```text
pgcheckup · app on db.example.com · PostgreSQL 17.6 · Amazon RDS

CRITICAL  xid-wraparound
          Table public.orders has used 1.61 billion of its 2.1 billion transaction IDs.
          Fix: VACUUM (FREEZE, VERBOSE) public.orders;

WARNING   long-transaction
          Session 4127 (worker on app) has had a transaction open for 6 days and has been idle in it for 6 days.
          Fix: if the session is stuck or abandoned, end it:
               SELECT pg_terminate_backend(4127);

WARNING   replication-slot-inactive
          Slot debezium has been inactive for 3 days and is holding 48 GB of WAL.
          Fix: restart its consumer, or drop the slot:
               SELECT pg_drop_replication_slot('debezium');

11 passed · 1 critical · 2 warnings · 1 skipped (wal-archiving-failing: managed by Amazon RDS)
```

## What it checks

- **Running out of IDs:** transaction ID and multixact wraparound, and `int4` sequences, identity columns and foreign keys near their limit.
- **Cleanup that can't run:** long transactions and sessions idle inside one, forgotten prepared transactions, no timeout for idle transactions, and autovacuum turned off.
- **Disks filling with WAL:** inactive replication slots, slots with no WAL limit, and WAL archiving that fails or hangs.
- **Capacity and settings:** connection saturation, `fsync` or `full_page_writes` turned off, invalid indexes, collations changed by an OS upgrade, and Postgres versions near or past end of life.

`pgcheckup list` shows every check.

Every finding says what breaks and how to fix it. `pgcheckup explain <check>` prints the full note, with links to incidents where it happened.

## Safe to run on production

- **Read-only.** Every check runs inside a read-only transaction with statement and lock timeouts. pgcheckup prints fixes. It never runs them.
- **Least privilege.** The built-in `pg_monitor` role is enough. `pgcheckup grant` prints the SQL for a checkup role, and you decide whether to run it.
- **Aware of managed Postgres.** RDS, Aurora, Cloud SQL, Azure, Supabase and Neon hide some views. Checks that can't run there are reported as skipped, with the reason.
- **No network beyond your database.** No telemetry and no update check. Reports never include query text, row data or your password.
- **Open source**, so you can check all of this.

## In CI

`pgcheckup scan` exits 1 when a finding reaches `--fail-on` (default `critical`), otherwise 2 when the scan couldn't run or a check errored, and 0 when neither happened. `--format json` ([its shape](docs/json.md)) and `--format markdown` are there for pipelines and pull requests. A baseline, so CI fails only on new findings, comes with v0.2.

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
