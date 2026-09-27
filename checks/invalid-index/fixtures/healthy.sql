CREATE TABLE fixture_accounts (email text);
INSERT INTO fixture_accounts VALUES ('ada@example.com'), ('grace@example.com');
CREATE UNIQUE INDEX CONCURRENTLY fixture_accounts_email ON fixture_accounts (email);
