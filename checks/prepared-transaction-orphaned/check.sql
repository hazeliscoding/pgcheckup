SELECT p.gid AS subject,
       p.database,
       quote_literal(p.gid) AS gid_literal,
       now() - p.prepared AS waiting_for
FROM pg_prepared_xacts AS p
WHERE p.prepared < now() - @min_age
ORDER BY p.prepared
