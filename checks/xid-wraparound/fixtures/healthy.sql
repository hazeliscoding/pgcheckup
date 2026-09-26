-- threshold warning_age = 10000
-- Freshly frozen, every table here is young. The other databases of a new cluster are too.
CREATE TABLE fixture_orders (id int);
VACUUM (FREEZE);
