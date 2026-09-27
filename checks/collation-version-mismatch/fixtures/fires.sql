-- An indexed column with an ICU collation, then the version Postgres recorded is set back, as
-- if the library had been upgraded since.
CREATE COLLATION fixture_collation (provider = icu, locale = 'en-US');
CREATE TABLE fixture_names (name text COLLATE fixture_collation);
CREATE INDEX fixture_names_name ON fixture_names (name);
UPDATE pg_collation SET collversion = '1.0' WHERE collname = 'fixture_collation';
