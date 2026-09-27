WITH sequences AS (
    SELECT s.seqrelid,
           format('%I.%I', n.nspname, c.relname) AS sequence_name,
           s.seqmax,
           pg_sequence_last_value(s.seqrelid) AS last_value
    FROM pg_sequence AS s
    JOIN pg_class AS c ON c.oid = s.seqrelid
    JOIN pg_namespace AS n ON n.oid = c.relnamespace
    WHERE s.seqincrement > 0
      -- Another session's temporary sequence can't be read.
      AND c.relpersistence <> 't'
),
feeds AS (
    -- The column a sequence feeds: 'a' for serial, 'i' for identity.
    SELECT d.objid AS seqrelid, d.refobjid AS table_oid, d.refobjsubid AS attnum
    FROM pg_depend AS d
    WHERE d.classid = 'pg_class'::regclass
      AND d.refclassid = 'pg_class'::regclass
      AND d.deptype IN ('a', 'i')
      AND d.refobjsubid > 0
),
columns AS (
    SELECT a.attrelid,
           a.attnum,
           format('%I.%I', n.nspname, c.relname) AS table_name,
           quote_ident(a.attname) AS attribute,
           format('%I.%I.%I', n.nspname, c.relname, a.attname) AS column_name,
           CASE a.atttypid
               WHEN 'int2'::regtype THEN 32767
               WHEN 'int4'::regtype THEN 2147483647
               ELSE 9223372036854775807
           END AS type_max
    FROM pg_attribute AS a
    JOIN pg_class AS c ON c.oid = a.attrelid
    JOIN pg_namespace AS n ON n.oid = c.relnamespace
    WHERE a.attnum > 0 AND NOT a.attisdropped
),
fed AS (
    SELECT s.*, col.table_name, col.attribute, col.column_name,
           least(s.seqmax, coalesce(col.type_max, s.seqmax)) AS capacity
    FROM sequences AS s
    LEFT JOIN feeds AS f ON f.seqrelid = s.seqrelid
    LEFT JOIN columns AS col ON col.attrelid = f.table_oid AND col.attnum = f.attnum
)
-- Sequences, measured against the column they feed, or their own maximum.
SELECT coalesce(fed.column_name, fed.sequence_name) AS subject,
       CASE WHEN fed.last_value >= fed.capacity * @critical_ratio THEN 'critical' END AS severity,
       fed.column_name,
       CASE WHEN fed.column_name IS NULL THEN fed.sequence_name END AS sequence_name,
       fed.table_name,
       fed.attribute,
       -- A serial column's sequence is often integer too, and needs changing with it.
       CASE WHEN fed.column_name IS NOT NULL AND fed.seqmax <= 2147483647 THEN fed.sequence_name END AS owned_sequence,
       NULL::text AS fk_column,
       NULL::text AS referenced,
       fed.last_value AS used,
       fed.capacity
FROM fed
WHERE fed.last_value >= fed.capacity * @warning_ratio
UNION ALL
-- Narrower foreign keys that point at a column a sequence feeds.
SELECT fk.column_name,
       CASE WHEN fed.last_value >= fk.type_max * @critical_ratio THEN 'critical' END,
       NULL,
       NULL,
       fk.table_name,
       fk.attribute,
       NULL,
       fk.column_name,
       fed.column_name,
       fed.last_value,
       fk.type_max
FROM pg_constraint AS k
JOIN columns AS fk ON fk.attrelid = k.conrelid AND fk.attnum = k.conkey[1]
JOIN columns AS target ON target.attrelid = k.confrelid AND target.attnum = k.confkey[1]
JOIN feeds AS f ON f.table_oid = k.confrelid AND f.attnum = k.confkey[1]
JOIN fed ON fed.seqrelid = f.seqrelid
WHERE k.contype = 'f'
  AND cardinality(k.conkey) = 1
  AND fk.type_max < target.type_max
  AND fed.last_value >= fk.type_max * @warning_ratio
ORDER BY 1
