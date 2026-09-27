-- Other databases in the cluster, as a whole. Those that don't allow connections (template0)
-- are frozen by autovacuum itself.
SELECT d.datname AS subject,
       CASE WHEN age(d.datfrozenxid) >= @critical_age THEN 'critical' END AS severity,
       d.datname AS database,
       NULL::text AS table_name,
       NULL::text AS temp_table,
       age(d.datfrozenxid)::bigint AS xid_age
FROM pg_database AS d
WHERE d.datallowconn
  AND d.datname <> current_database()
  AND age(d.datfrozenxid) >= @warning_age
UNION ALL
-- Tables in this database. A TOAST table is vacuumed with its table, so it counts toward it.
SELECT * FROM (
    SELECT format('%I.%I', n.nspname, c.relname),
           CASE WHEN greatest(age(c.relfrozenxid), age(t.relfrozenxid)) >= @critical_age THEN 'critical' END,
           NULL::text,
           CASE WHEN c.relpersistence <> 't' THEN format('%I.%I', n.nspname, c.relname) END,
           CASE WHEN c.relpersistence = 't' THEN format('%I.%I', n.nspname, c.relname) END,
           greatest(age(c.relfrozenxid), age(t.relfrozenxid))::bigint
    FROM pg_class AS c
    JOIN pg_namespace AS n ON n.oid = c.relnamespace
    LEFT JOIN pg_class AS t ON t.oid = c.reltoastrelid
    WHERE c.relkind IN ('r', 'm')
      AND greatest(age(c.relfrozenxid), age(t.relfrozenxid)) >= @warning_age
    ORDER BY 6 DESC
    LIMIT 20
) AS oldest
