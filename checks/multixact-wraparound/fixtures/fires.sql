-- threshold warning_age = 1
-- Locking a row, then updating it in a savepoint, makes one multixact, which ages every table
-- that hasn't been frozen since.
CREATE TABLE fixture_accounts (id int PRIMARY KEY, balance int);
INSERT INTO fixture_accounts VALUES (1, 0);
BEGIN;
SELECT * FROM fixture_accounts FOR SHARE;
SAVEPOINT fixture;
UPDATE fixture_accounts SET balance = 1;
COMMIT;
