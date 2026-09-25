-- The role may insert, so only the READ ONLY transaction stands in the way.
CREATE TABLE public.fixture_written (n int);
GRANT INSERT ON public.fixture_written TO checkup;
