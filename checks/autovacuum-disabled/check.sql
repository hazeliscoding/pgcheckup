SELECT s.name AS subject,
       s.name AS setting,
       NULL::text AS table_name
FROM pg_settings AS s
WHERE s.name IN ('autovacuum', 'track_counts') AND s.setting = 'off'
UNION ALL
-- Tables in this database. The option is stored as written, in any spelling Postgres accepts
-- for false, and a TOAST table carries its own.
SELECT format('%I.%I', n.nspname, c.relname),
       NULL,
       format('%I.%I', n.nspname, c.relname)
FROM pg_class AS c
JOIN pg_namespace AS n ON n.oid = c.relnamespace
LEFT JOIN pg_class AS t ON t.oid = c.reltoastrelid
WHERE c.relkind IN ('r', 'm')
  AND EXISTS (
      SELECT FROM unnest(c.reloptions || coalesce(t.reloptions, '{}')) AS o(option)
      WHERE o.option ~* '^autovacuum_enabled=(f(a(l(se?)?)?)?|off?|no?|0)$'
  )
