WITH available AS (
    -- reserved_connections arrived in Postgres 16.
    SELECT current_setting('max_connections')::int
           - current_setting('superuser_reserved_connections')::int
           - coalesce(current_setting('reserved_connections', true)::int, 0) AS slots
),
used AS (
    SELECT count(*) AS sessions FROM pg_stat_activity WHERE backend_type = 'client backend'
)
SELECT 'max_connections' AS subject,
       CASE WHEN u.sessions >= a.slots * @critical_ratio THEN 'critical' END AS severity,
       u.sessions AS used,
       a.slots AS available
FROM available AS a, used AS u
WHERE u.sessions >= a.slots * @warning_ratio
