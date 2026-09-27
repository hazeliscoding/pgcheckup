SELECT s.name AS subject,
       s.setting,
       CASE s.name WHEN 'zero_damaged_pages' THEN 'off' ELSE 'on' END AS safe_value
FROM pg_settings AS s
WHERE (s.name IN ('fsync', 'full_page_writes') AND s.setting = 'off')
   OR (s.name = 'zero_damaged_pages' AND s.setting = 'on')
ORDER BY s.name
