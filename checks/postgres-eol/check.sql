-- End-of-life dates from https://www.postgresql.org/support/versioning/. Add each new major here.
WITH eol(major, ends) AS (
    VALUES (10, date '2022-11-10'), (11, date '2023-11-09'), (12, date '2024-11-21'),
           (13, date '2025-11-13'), (14, date '2026-11-12'), (15, date '2027-11-11'),
           (16, date '2028-11-09'), (17, date '2029-11-08'), (18, date '2030-11-14')
)
SELECT e.major::text AS subject,
       CASE WHEN e.ends > now() THEN 'info' END AS severity,
       CASE WHEN e.ends <= now() THEN to_char(e.ends, 'YYYY-MM-DD') END AS ended_on,
       CASE WHEN e.ends > now() THEN to_char(e.ends, 'YYYY-MM-DD') END AS ends_on,
       CASE WHEN e.ends > now() THEN (e.ends - current_date) * interval '1 day' END AS time_left,
       (SELECT max(major) FROM eol) AS latest
FROM eol AS e
WHERE e.major = current_setting('server_version_num')::int / 10000
  AND e.ends - @warn_before <= now()
