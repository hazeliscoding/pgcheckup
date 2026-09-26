-- threshold warning_age = 3
-- Consuming transaction IDs ages every table that hasn't been frozen since.
CREATE TABLE fixture_orders (id int);
SELECT txid_current();
SELECT txid_current();
SELECT txid_current();
SELECT txid_current();
