-- The session's own value covers the server and anything set for pgcheckup's role and database;
-- pg_db_role_setting covers what is set for the application's roles and databases.
SELECT 'idle_in_transaction_session_timeout' AS subject
WHERE current_setting('idle_in_transaction_session_timeout') = '0'
  -- transaction_timeout arrived in Postgres 17, and ends idle transactions too.
  AND coalesce(current_setting('transaction_timeout', true), '0') = '0'
  AND NOT EXISTS (
      SELECT FROM pg_db_role_setting AS s, unnest(s.setconfig) AS c(setting)
      WHERE c.setting ~ '^(idle_in_transaction_session_timeout|transaction_timeout)='
        AND c.setting !~ '=0$'
  )
