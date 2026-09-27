-- A unique index over duplicate values fails half way and stays behind, invalid.
CREATE TABLE fixture_accounts (email text);
INSERT INTO fixture_accounts VALUES ('ada@example.com'), ('ada@example.com');
-- expect error
CREATE UNIQUE INDEX CONCURRENTLY fixture_accounts_email ON fixture_accounts (email);
