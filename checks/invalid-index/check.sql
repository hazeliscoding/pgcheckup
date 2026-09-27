SELECT format('%I.%I', n.nspname, i.relname) AS subject,
       format('%I.%I', n.nspname, t.relname) AS table_name
FROM pg_index AS x
JOIN pg_class AS i ON i.oid = x.indexrelid
JOIN pg_class AS t ON t.oid = x.indrelid
JOIN pg_namespace AS n ON n.oid = i.relnamespace
WHERE NOT x.indisvalid
  -- A partitioned table's index stays invalid until every partition has its own.
  AND i.relkind <> 'I'
  -- An index still being built is invalid until the build finishes.
  AND NOT EXISTS (SELECT FROM pg_stat_progress_create_index AS p WHERE p.index_relid = x.indexrelid)
ORDER BY 1
