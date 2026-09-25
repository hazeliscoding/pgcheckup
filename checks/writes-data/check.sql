WITH written AS (
    INSERT INTO public.fixture_written VALUES (1) RETURNING n
)
SELECT n::text AS subject FROM written
