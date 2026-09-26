-- Set for the database, it applies to every new session there.
ALTER DATABASE app SET idle_in_transaction_session_timeout = '10min';
