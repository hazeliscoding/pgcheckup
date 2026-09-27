-- threshold warn_before = 0s
-- A supported version, with no notice, has nothing to report. Once Postgres 14 passes its end
-- of life (2026-11-12), this fails on 14, which is the signal to drop it from the CI matrix.
SELECT 1;
