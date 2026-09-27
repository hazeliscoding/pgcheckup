-- server autovacuum = off
-- Off for the server, and for a table left over from a bulk load.
CREATE TABLE fixture_events (id int) WITH (autovacuum_enabled = off);
