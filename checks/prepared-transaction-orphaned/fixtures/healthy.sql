-- server max_prepared_transactions = 5
-- threshold min_age = 0s
-- A prepared transaction that was committed is gone.
CREATE TABLE fixture_payments (id int);
BEGIN;
INSERT INTO fixture_payments VALUES (1);
PREPARE TRANSACTION 'fixture';
COMMIT PREPARED 'fixture';
