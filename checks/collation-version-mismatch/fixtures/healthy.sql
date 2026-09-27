CREATE COLLATION fixture_collation (provider = icu, locale = 'en-US');
CREATE TABLE fixture_names (name text COLLATE fixture_collation);
CREATE INDEX fixture_names_name ON fixture_names (name);
