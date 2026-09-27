---
id: integer-exhaustion
title: Integer IDs running out
category: ids
severity: warning
min_version: 14
privileges: [select_on_sequences]
thresholds:
  warning_ratio: 0.5
  critical_ratio: 0.8
message: "[Column {column_name} has used {used:count} of the {capacity:count} values it can hold.][Sequence {sequence_name} has used {used:count} of the {capacity:count} values it can hold.][Column {fk_column} holds values up to {capacity:count}, but it is a foreign key to {referenced}, which has reached {used:count}.]"
fix: "[move it to bigint. This rewrites the table under an exclusive lock, so plan it:\nALTER TABLE {table_name} ALTER COLUMN {attribute} TYPE bigint;][\nALTER SEQUENCE {owned_sequence} AS bigint;][ALTER SEQUENCE {sequence_name} AS bigint;]"
---

## What breaks

An `integer` column holds values up to about 2.1 billion (`smallint`, 32,767). When the sequence that feeds it reaches that, every insert fails with "integer out of range" or "nextval: reached maximum value", and the application stops taking new rows.

Three shapes lead there:

- A `serial` or identity column declared `integer`, often from before anyone expected the table to grow.
- A sequence declared `AS integer` on its own, or left `integer` when its column was changed to `bigint`.

Sequences with `CYCLE` start over by design, so the check ignores them.
- An `integer` foreign key that points at a `bigint` key. The key is fine, but once its values pass 2.1 billion, no row can reference them.

Reading sequence counters needs SELECT on the sequences, which shows counters but no table rows. `pgcheckup grant` prints that grant. Without it, the check is skipped.

## Fix

Change the column to `bigint`, and its sequence with it:

```sql
ALTER TABLE public.orders ALTER COLUMN id TYPE bigint;
ALTER SEQUENCE public.orders_id_seq AS bigint;
```

Changing a column's type rewrites the table and blocks it for the whole time, so plan it: on a large table, add a new `bigint` column, backfill it in batches, and swap it in.

## Seen in

- [Incident 2558: Heroku API unavailable](https://status.heroku.com/incidents/2558), Heroku
- [Sequence manipulation functions](https://www.postgresql.org/docs/current/functions-sequence.html), PostgreSQL documentation
