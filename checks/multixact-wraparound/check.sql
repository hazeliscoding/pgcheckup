-- Other databases in the cluster, as a whole. Those that don't allow connections (template0)
-- are frozen by autovacuum itself.
SELECT d.datname AS subject,
       CASE WHEN mxid_age(d.datminmxid) >= @critical_age THEN 'critical' END AS severity,
       d.datname AS database,
       NULL::text AS table_name,
       mxid_age(d.datminmxid)::bigint AS mxid_age
FROM pg_database AS d
WHERE d.datallowconn
  AND d.datname <> current_database()
  AND mxid_age(d.datminmxid) >= @warning_age
UNION ALL
-- Tables in this database. A TOAST table is vacuumed with its table, so it counts toward it.
SELECT * FROM (
    SELECT format('%I.%I', n.nspname, c.relname),
           CASE WHEN greatest(mxid_age(c.relminmxid), mxid_age(t.relminmxid)) >= @critical_age THEN 'critical' END,
           NULL::text,
           format('%I.%I', n.nspname, c.relname),
           greatest(mxid_age(c.relminmxid), mxid_age(t.relminmxid))::bigint
    FROM pg_class AS c
    JOIN pg_namespace AS n ON n.oid = c.relnamespace
    LEFT JOIN pg_class AS t ON t.oid = c.reltoastrelid
    WHERE c.relkind IN ('r', 'm')
      -- Only the session that owns a temporary table can vacuum it.
      AND c.relpersistence <> 't'
      AND greatest(mxid_age(c.relminmxid), mxid_age(t.relminmxid)) >= @warning_age
    ORDER BY 5 DESC
    LIMIT 20
) AS oldest
