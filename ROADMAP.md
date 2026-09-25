# Roadmap

pgcheckup is a read-only CLI (.NET 10, NativeAOT) that checks a PostgreSQL database for the problems that cause outages, explains each one in plain words and gives the fix. It is for teams without a DBA. This file tracks what gets built, in what order, and the decisions already made.

## Decisions (2026-09-25)

- **Name.** The idea started as "Postgres Doctor". `pgdoctor` is already an existing Go project, so this one is pgcheckup. PostgresAI's `postgres-checkup` is unrelated, and the README says so.
- **Position.** Production readiness for teams without a DBA. It has fewer checks than pg-healthcheck or pgdoctor, and each one maps to a failure that causes outages and is explained in plain words with its fix. It is not a DBA toolkit.
- **Read-only by construction.** A scan opens one connection with `default_transaction_read_only = on`, `application_name = pgcheckup`, `statement_timeout` (5 s) and `lock_timeout` (1 s). Every check runs inside its own `READ ONLY` transaction. The lock timeout stops a scan from queueing behind a migration's lock and then blocking the traffic behind it. pgcheckup prints fixes and never runs them.
- **Least privilege.** No check needs more than `pg_monitor`. Each check declares what it needs, and if the role doesn't have it, the check is skipped with the reason. `pgcheckup grant` prints the SQL for a checkup role.
- **SQL only.** pgcheckup talks only to Postgres. Provider settings that SQL can see (such as `rds.force_ssl`) are in scope. Checks that need a cloud API (RDS backups, deletion protection, encryption at rest) are not.
- **Managed providers.** pgcheckup detects RDS/Aurora, Cloud SQL, Azure Database for PostgreSQL, Supabase and Neon from SQL. A check can list providers where it doesn't apply or can't run, and it shows as skipped there, with the reason.
- **Checks are SQL plus Markdown**, one folder per check under `checks/<id>/`:
  - `check.sql`: one read-only query that returns findings in a fixed shape (`subject`, `severity`, `detail`, plus named values for the message);
  - `check.md`: frontmatter (id, title, category, default severity, thresholds, minimum Postgres version, required privileges, providers to skip) and a body with **What breaks**, **Fix** and **Seen in** (links to public incidents or the Postgres docs);
  - `fixtures/fires.sql` and `fixtures/healthy.sql`: setup scripts. The check must report on the first and stay quiet on the second.
  Checks are embedded in the binary at build time, and the build fails on invalid frontmatter.
- **Thresholds** live in each check's frontmatter, with defaults. `check.sql` reads them as parameters, so a fixture can lower a threshold when the real condition can't be reproduced at full scale (wraparound, for example). Overrides from a config file come in v0.2.
- **Ages and durations** are computed in SQL from the server's clock, so a skewed client clock can't change a finding.
- **Postgres versions:** every community-supported major (14 to 18 today), each tested in CI with Testcontainers. When a major reaches end of life, it moves to best effort, and scanning it reports the end of life as a finding.
- **Output:** terminal (default), `--format json` and `--format markdown` in v0.1. The JSON has a `schema` version. The HTML report comes in v0.2.
- **Exit codes:** 0 when no finding reaches `--fail-on` (default `critical`), 1 when one does, and 2 when the scan couldn't run. Skipped checks never fail a scan.
- **Severity:** `critical` can take the database down or lose data soon. `warning` is heading there, or removes a safety net. `info` is housekeeping.
- **Connection:** a `postgres://` URL, a key-value connection string, or the standard `PG*` environment variables and `.pgpass`. Docs keep passwords off the command line.
- **What output may contain.** Findings name database objects (tables, slots, roles), settings, process IDs and durations. They never include query text, row data, passwords or client addresses.
- **No network** beyond the Postgres connection. No telemetry and no update check. End-of-life dates ship inside each release.
- **Runtime:** .NET 10 (LTS) with NativeAOT, and Npgsql through `NpgsqlSlimDataSourceBuilder`. Trim and AOT warnings are errors.
- **Distribution:** NativeAOT binaries for linux-x64, linux-arm64, osx-arm64 and win-x64 on GitHub Releases, with SHA-256 checksums, plus a container image on GHCR for CI. NativeAOT can't cross-compile between operating systems, so releases build on a runner matrix. A `dotnet tool` package comes in v0.2.
- **The paid path comes later.** The CLI stays free and complete. A one-time audit report, hosted monitoring and a team dashboard are listed under Later and get built only if public signals show demand. They would be separate code.
- **Brand:** pending. The logo comes from Claude Design and goes in `docs/brand/`, with `-dark` variants.
- **License:** Apache-2.0.

## M0: Placeholder (as soon as possible)

- [x] Add `LICENSE` (Apache-2.0).
- [ ] Scaffold the .NET 10 solution: a `pgcheckup` console app with NativeAOT on, and a test project. `pgcheckup --version` and `pgcheckup list` work.
- [ ] Check contract: the `checks/<id>/` layout, frontmatter validation at build time, and embedding in the binary. One real check end to end: `replication-slot-inactive`.
- [ ] Fixture runner on Testcontainers: for every check, `fires.sql` must produce a finding and `healthy.sql` must not.
- [ ] Read-only guard: the fixture runner runs each check as a role that has only `pg_monitor`, inside a `READ ONLY` transaction. Positive control: a test check that writes must fail.
- [ ] `pgcheckup scan` with terminal output and exit codes 0, 1 and 2.
- [ ] CI on every PR: build, fixture tests on Postgres 14 to 18, and a NativeAOT publish that fails on any IL2xxx or IL3xxx warning.
- [ ] Brand: the logo in `docs/brand/`, with `-dark` variants. Then replace the README heading with a `<picture>` lockup.
- [ ] Add a recording or screenshot of a scan to the README.

**Done when:** CI is green, the NativeAOT binary reports an inactive replication slot on a Testcontainers Postgres and exits 1 with `--fail-on warning`, and a test PR that adds a writing check fails the read-only guard.

## M1: Engine and check catalog

- [ ] Engine: session guards, server version and provider detection, and a privilege probe, followed by every applicable check in its own read-only transaction. A check that errors or times out is reported as errored, and the others still run.
- [ ] Provider detection, tested by simulating each provider's roles and settings in fixtures.
- [ ] Desk research for the catalog: go through public Postgres postmortems (danluu/post-mortems, engineering blogs) and DBA Stack Exchange, list the failures that recur, and adjust the table below to match. Every check gets at least one **Seen in** link.
- [ ] The v0.1 checks:

| Check | Catches |
|---|---|
| `xid-wraparound` | Databases and tables whose oldest unfrozen transaction ID is nearing the 2.1 billion limit |
| `multixact-wraparound` | The same for multixact IDs |
| `long-transaction` | Transactions open longer than the threshold, which hold back vacuum |
| `idle-in-transaction` | Sessions idle inside an open transaction, and `idle_in_transaction_session_timeout` left unset |
| `prepared-transaction-orphaned` | Prepared transactions left behind, which hold locks and block vacuum |
| `replication-slot-inactive` | Slots with no consumer, which keep WAL until the disk fills |
| `replication-slot-unbounded` | `max_slot_wal_keep_size = -1`, so one stuck slot can keep unlimited WAL |
| `wal-archiving-failing` | `archive_command` failing since the last success, which breaks point-in-time recovery |
| `connection-saturation` | Connections close to `max_connections` minus the reserved slots |
| `dangerous-settings` | `fsync`, `full_page_writes` or `autovacuum` turned off |
| `autovacuum-disabled-table` | Tables with `autovacuum_enabled = false` |
| `integer-exhaustion` | `int4` sequences and identity columns past a share of their range |
| `invalid-index` | Indexes left invalid by a failed `CREATE INDEX CONCURRENTLY`, which slow writes and are never used |
| `postgres-eol` | Major versions past their end-of-life date |

- [ ] `pgcheckup explain <check>` prints the check's note. `pgcheckup list` shows every check with its category and minimum version.
- [ ] `pgcheckup grant` prints SQL for a least-privilege checkup role (`pg_monitor` plus `CONNECT`).
- [ ] `--format json` and `--format markdown`. The JSON shape is documented, with `"schema": 1`.

**Done when:** every check's fixtures pass on Postgres 14 to 18, and a scan as a role with only `pg_monitor` either runs or skips (with a reason) every check, with no errors.

## M2: v0.1.0

- [ ] Release workflow: NativeAOT binaries for linux-x64, linux-arm64, osx-arm64 and win-x64 on GitHub Releases, with SHA-256 checksums.
- [ ] Container image on GHCR (amd64, arm64), and a GitHub Actions example in the README.
- [ ] Error messages: connection, TLS, authentication and missing-privilege failures each say what to do next.
- [ ] Dogfooding log: scan real databases, including at least one managed provider, and record every false alarm and every missed problem.
- [ ] Launch gates: README quick start tested on a clean machine, supported platforms and Postgres versions documented, `CONTRIBUTING.md` with build and test steps, `SECURITY.md`, no known critical bugs, and green CI.

**Done when:** a released binary on a clean machine scans a local Postgres and a managed one, the results match the dogfooding log, and the README's GitHub Actions example fails the job when a finding reaches `--fail-on`.

## M3: v0.2, reports, baselines and contributors

- [ ] HTML report: one self-contained file with no remote assets, ready to share with a team or a client.
- [ ] Baseline: `pgcheckup baseline` records the accepted findings, and `scan --baseline` fails only on new ones. Findings are matched by check id and subject, not by message text.
- [ ] Config file for thresholds and ignored checks.
- [ ] Housekeeping checks at `info` severity: unused indexes, foreign keys without an index, and estimated table and index bloat.
- [ ] `dotnet tool install -g pgcheckup`, with a NativeAOT package per platform.
- [ ] `CONTRIBUTING.md`: how to add a check in one folder, with a check template.
- [ ] Issue templates: "False alarm", "Missed problem" and "New check". They ask for the check id and pgcheckup's output, never data or connection strings.
- [ ] Good-first-issue list of checks.

## Later

- A paid one-time audit report
- Hosted monitoring (scheduled scans with alerts), then a team dashboard
- Scanning every database in a cluster in one run
- Checks based on `pg_stat_statements`
- Scoop, Homebrew and winget packages
- Signed releases with build provenance

## Not planned

- Running fixes. pgcheckup prints the SQL, and you decide when to run it.
- Time-series monitoring, dashboards or alerting in the CLI. That is the hosted product under Later, or tools such as pganalyze and pgwatch.
- Query tuning and EXPLAIN analysis. pganalyze and pgMustard cover it.
- Cloud API checks (backups, snapshots, deletion protection, encryption at rest). They need cloud credentials, and pgcheckup needs only a Postgres role.
- Databases other than PostgreSQL, including Postgres-compatible ones.

## How we'll know it works

Evidence comes from dogfooding (n=1, recorded in the log). After release it also comes from public signals: downloads and image pulls, "false alarm" and "missed problem" issues, and check PRs from strangers. The paid steps under Later need that evidence first.
