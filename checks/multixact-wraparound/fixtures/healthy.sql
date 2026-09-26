-- threshold warning_age = 1
-- With no multixact made, nothing has aged, even at the lowest threshold.
CREATE TABLE fixture_accounts (id int PRIMARY KEY, balance int);
INSERT INTO fixture_accounts VALUES (1, 0);
UPDATE fixture_accounts SET balance = 1;
