-- Each database's default collation, recorded from Postgres 15 on.
SELECT d.datname AS subject,
       d.datname AS database,
       quote_ident(d.datname) AS database_ident,
       NULL::text AS collation_name,
       d.datcollversion AS recorded,
       v.actual
FROM pg_database AS d
CROSS JOIN LATERAL (SELECT pg_database_collation_actual_version(d.oid) AS actual) AS v
WHERE d.datallowconn
  AND d.datcollversion IS NOT NULL
  AND v.actual IS NOT NULL
  AND v.actual <> d.datcollversion
UNION ALL
-- Collations that indexes use in this database. Checking only those avoids asking ICU for the
-- version of each of the hundreds of collations Postgres imports.
SELECT format('%I.%I', n.nspname, c.collname),
       NULL,
       NULL,
       format('%I.%I', n.nspname, c.collname),
       c.collversion,
       v.actual
FROM pg_collation AS c
JOIN pg_namespace AS n ON n.oid = c.collnamespace
CROSS JOIN LATERAL (SELECT pg_collation_actual_version(c.oid) AS actual) AS v
WHERE c.oid IN (SELECT unnest(x.indcollation::oid[]) FROM pg_index AS x)
  AND c.collversion IS NOT NULL
  AND v.actual IS NOT NULL
  AND v.actual <> c.collversion
ORDER BY 1
