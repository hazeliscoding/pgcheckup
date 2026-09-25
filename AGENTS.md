# AGENTS.md

These are the working rules for agents in this repo. pgcheckup is a read-only CLI (.NET 10 NativeAOT, Apache-2.0) that checks a PostgreSQL database for the problems that cause outages and explains each one with its fix, for teams without a DBA.

## Sources of truth

- `README.md`: the pitch and the "safe to run on production" promises.
- `ROADMAP.md`: decisions already made, the milestones, and what is out of scope. Check it before proposing features. Respect those decisions unless the owner reopens them. Record new or changed decisions there, with the date.
- Work follows the milestones in `ROADMAP.md`. Don't build past the current milestone without asking.

## Commands

Keep commands cross-platform (`dotnet`, `docker`), because the owner develops on Windows. Avoid bash-only scripts.

- Build: `dotnet build pgcheckup.slnx`. A broken check folder fails the build with its file and line.
- Test: `dotnet test --project tests/Pgcheckup.Tests`. It needs Docker, and uses Postgres 18 unless `PGCHECKUP_TEST_POSTGRES` names another major (14 to 17). CI runs all five.
- Publish: `dotnet publish src/Pgcheckup -c Release -r win-x64 -o out` (`linux-x64` on Linux). Trim and AOT warnings fail it.
- Test the published binary: set `PGCHECKUP_BINARY` to it, then run `dotnet test --project tests/Pgcheckup.Tests -- --filter-class Pgcheckup.Tests.Cli.NativeBinaryTests`.
- NativeAOT publish on this Windows machine fails with `'vswhere.exe' is not recognized` unless the VS Installer folder is on PATH. Run it as `$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"; dotnet publish …`. That is an environment problem, not an AOT warning.

## Safe to run on production (hard rules)

The product is only as good as these rules. Never break them, not even in debug modes or dev tooling.

- **Read-only, always.** A check is one `SELECT` against catalogs and statistics views. No DDL or DML, and no functions with side effects: `pg_terminate_backend`, `pg_cancel_backend`, `pg_reload_conf`, `pg_stat_reset*`, `pg_switch_wal`, `pg_create_*`, `pg_drop_*`, `pg_advisory_*`, `nextval`, `setval`, `set_config`, `txid_current`. Every statement pgcheckup sends runs inside `BEGIN READ ONLY` with `SET LOCAL statement_timeout`, `lock_timeout` and `search_path = pg_catalog, pg_temp`, then rolls back. Never set anything for the whole session, because behind a transaction pooler it reaches the app's connections. Never weaken or bypass these guards.
- **Fixes are text.** pgcheckup prints fix SQL and never executes it.
- **Least privilege.** No check needs more than `pg_monitor`. Never require superuser or `rds_superuser`. If the role lacks a privilege, the check is skipped with the reason. It is never an error.
- **No network beyond the Postgres connection.** No telemetry, update checks, crash reporting or remote lookups. Data such as end-of-life dates ships inside the release.
- **No secrets or data in output.** Never print or log a password or a full connection string, query text (`pg_stat_activity.query`, `pg_stat_statements.query`), row data or client addresses. Findings name objects, settings, process IDs and durations only.
- **Placeholders everywhere.** Docs, fixtures, tests and issues use `db.example.com`, `app` and `checkup`, never real hosts or credentials.
- **Tests run against Testcontainers only**, never a real database.

## Checks

- One folder per check: `checks/<id>/check.sql`, `check.md`, `fixtures/fires.sql` and `fixtures/healthy.sql`. The shape is in the `ROADMAP.md` decisions.
- `check.sql` is one read-only query that returns values, never prose. The wording lives in the `message` and `fix` templates in `check.md`. Thresholds come in as `@name` parameters and are never hard-coded. The search path is `pg_catalog` only, so qualify anything in another schema.
- Compute ages and durations in SQL from the server's `now()`, not the client's clock.
- `check.md` has **What breaks**, **Fix** and **Seen in** sections. Every check has at least one **Seen in** link to a public incident or the Postgres docs. Never cite anything a reader can't open.
- Both fixtures are required. `fires.sql` is the positive control, so a check without one isn't done. A fixture may lower a threshold (`-- threshold name = value`) when the real condition can't be reproduced at scale.
- Check ids are kebab-case and stable, because baselines and ignore lists depend on them. Renaming one is a breaking change that needs a decision in `ROADMAP.md`.
- Severity: `critical` can take the database down or lose data soon. `warning` is heading there, or removes a safety net. `info` is housekeeping. Don't inflate severity.
- A check declares its minimum Postgres version and the providers where it is skipped. Every check is tested on every supported version.

## .NET and NativeAOT

- .NET 10 with NativeAOT. Trim and AOT warnings (IL2xxx, IL3xxx) are errors. Never suppress one without a decision in `ROADMAP.md`.
- No reflection-based serialization. Use System.Text.Json source generation.
- Use `NpgsqlSlimDataSourceBuilder` and turn on only the features the checks need.
- Code that reads the client's clock (end-of-life dates, for example) takes a `TimeProvider`, and tests use `FakeTimeProvider`.

## CLI output and copy

- Voice is calm, short and declarative. No exclamation marks, no emoji.
- Every finding says what is wrong, why it matters and what to do: "Table orders has used 1.61 billion of its 2.1 billion transaction IDs."
- Never call a database "healthy". Say "11 passed · 2 skipped".
- Severity is always a word, never only a color. Respect `NO_COLOR` and plain output when stdout isn't a terminal.
- Times use the 24-hour clock, in UTC unless labelled. Large numbers are human-readable (1.61 billion, 48 GB).
- Exit codes (0, 1, 2), check ids and the JSON `schema` are contracts. Change them only through a decision in `ROADMAP.md`.
- Write the name in lowercase: pgcheckup.

## Brand

- The logo is option 1A, "Scan", from Claude Design. Don't redraw or restyle it.
- The assets are in `docs/brand/`: `mark.svg` and `lockup.svg` for light backgrounds, and `-dark` files for dark backgrounds.
- The wordmark is Bricolage Grotesque SemiBold (optical size 34, letter spacing -0.02em), converted to vector paths. Use the SVGs, and don't re-typeset it with a web font. "pg" is Postgres blue: `#336791`, or `#5B9BD5` on dark.
- Ink is `#1B1E22` on light backgrounds and `#F7F8F9` on dark.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it adds clarity (`feat(checks): …`, `feat(engine): …`).
- **No AI attribution** in commits or PRs. That means no `Co-Authored-By` trailers, no "Generated with" lines and no session links.
- **`AGENTS.md` and `CLAUDE.md` are committed.** `.gitignore` un-ignores them, overriding the global gitignore. Keep them free of secrets and private paths.
- **Checks:** automate acceptance checks instead of handing manual steps to the owner. Give every check that tests for an absence a positive control, meaning a case that proves the check can fail.
- **Validation:** evidence comes from dogfooding (the log) and public async signals (issues, PRs, downloads, image pulls). Don't plan interviews, recruiting or outreach.
- **Docs:** short and concise. Prefer editing `ROADMAP.md` over creating new planning documents. Repo files never reference the owner's private notes.
- **Code comments:** explain why, not what. Only comment on what the code can't say for itself: a non-obvious constraint, a workaround and its cause, or a line that keeps a hard rule. Don't restate names or types, don't add boilerplate XML docs, and don't leave commented-out code. A check's `check.md` is its documentation.
