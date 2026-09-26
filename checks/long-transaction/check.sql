SELECT a.pid::text AS subject,
       a.usename AS role_name,
       a.datname AS database,
       now() - a.xact_start AS open_for,
       CASE WHEN a.state LIKE 'idle in transaction%' THEN now() - a.state_change END AS idle_for
FROM pg_stat_activity AS a
WHERE a.backend_type = 'client backend'
  AND a.pid <> pg_backend_pid()
  AND (a.xact_start < now() - @min_duration
       OR (a.state LIKE 'idle in transaction%' AND a.state_change < now() - @min_idle))
ORDER BY a.xact_start
