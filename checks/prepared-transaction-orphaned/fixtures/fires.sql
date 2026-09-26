-- server max_prepared_transactions = 5
-- threshold min_age = 0s
CREATE TABLE fixture_payments (id int);
BEGIN;
INSERT INTO fixture_payments VALUES (1);
PREPARE TRANSACTION 'fixture';
