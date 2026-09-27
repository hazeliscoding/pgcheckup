WITH settings AS (
    SELECT current_setting('archive_mode') AS archive_mode,
           -- archive_library arrived in Postgres 15. Neither value is ever returned: either can
           -- hold credentials.
           current_setting('archive_command') = ''
               AND coalesce(current_setting('archive_library', true), '') = '' AS unset
),
archiver AS (
    SELECT coalesce(a.last_failed_time > coalesce(a.last_archived_time, '-infinity'), false) AS failing,
           coalesce(a.last_archived_time, a.stats_reset, pg_postmaster_start_time()) AS good_since
    FROM pg_stat_archiver AS a
),
ready AS (
    SELECT count(*) AS segments, min(modification) AS oldest
    FROM pg_ls_archive_statusdir()
    WHERE name LIKE '%.ready'
)
SELECT 'archive_command' AS subject,
       s.archive_mode AS unset_mode,
       NULL::interval AS failing_for,
       NULL::bigint AS ready_count,
       NULL::interval AS waiting_for
FROM settings AS s
WHERE s.archive_mode <> 'off' AND s.unset
UNION ALL
SELECT 'archiver', NULL, now() - a.good_since, NULL, NULL
FROM settings AS s, archiver AS a
WHERE s.archive_mode <> 'off' AND NOT s.unset AND a.failing
UNION ALL
-- A hung archiver logs no failure, so only the age of the waiting segments shows it.
SELECT 'archive_status', NULL, NULL, r.segments, now() - r.oldest
FROM settings AS s, archiver AS a, ready AS r
WHERE s.archive_mode <> 'off' AND NOT s.unset AND NOT a.failing
  AND r.oldest < now() - @max_ready_age
