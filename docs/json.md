# JSON report

`pgcheckup scan --format json` writes one JSON object. Its shape is versioned by `schema`. Adding a field doesn't change the version. Removing or renaming one, or changing what a value means, raises it. The exit codes are the same as for the terminal report.

```json
{
  "schema": 1,
  "pgcheckup": "0.1.0",
  "server": {
    "database": "app",
    "host": "db.example.com",
    "version": "17.6",
    "provider": { "id": "rds", "name": "Amazon RDS" }
  },
  "summary": { "passed": 11, "critical": 0, "warning": 1, "info": 0, "errored": 0, "skipped": 1 },
  "checks": [
    {
      "id": "replication-slot-inactive",
      "title": "Inactive replication slot",
      "category": "wal",
      "status": "warning",
      "findings": [
        {
          "subject": "debezium",
          "severity": "warning",
          "message": "Slot debezium has been inactive for 3 days and is holding 48 GB of WAL.",
          "fix": "restart its consumer, or drop the slot:\nSELECT pg_drop_replication_slot('debezium');",
          "values": { "subject": "debezium", "inactive_for": 259200, "retained_wal": 51539607552, "xmin_age": null }
        }
      ]
    },
    {
      "id": "wal-archiving-failing",
      "title": "WAL archiving failing",
      "category": "wal",
      "status": "skipped",
      "reason": "managed by Amazon RDS",
      "findings": []
    }
  ]
}
```

## Fields

| Field | Meaning |
| --- | --- |
| `schema` | The version of this shape. Currently `1`. |
| `pgcheckup` | The version of pgcheckup that wrote the report. |
| `server.database`, `server.host` | The database scanned and the host as given. The connection string and password are never included. |
| `server.version` | The Postgres version, such as `17.6`. |
| `server.provider` | The managed service detected (`id` and `name`), or `null` for a self-managed server. |
| `summary` | How many checks passed, and how many ended at each severity. Each check counts once, at its worst finding. |
| `checks[].id` | The check's stable id. `pgcheckup explain <id>` describes it. |
| `checks[].status` | `passed`, `critical`, `warning`, `info` (its worst finding), `skipped` or `errored`. |
| `checks[].reason` | Why the check was skipped or errored. Only present for those two statuses. |
| `checks[].findings[].subject` | The object the finding is about, such as a slot or table name. |
| `checks[].findings[].severity` | `critical`, `warning` or `info`. |
| `checks[].findings[].message`, `.fix` | The text the terminal report prints. pgcheckup never runs the fix. |
| `checks[].findings[].values` | The facts behind the message, one per column it uses: sizes and counts as numbers, durations in seconds, timestamps as ISO 8601 UTC strings. A value the message left out, because it was NULL, is `null`. The names differ per check. |
