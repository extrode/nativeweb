# db

PostgreSQL schema and the inline SQL consumed by JauntyQ.

```
migrations/   ordered PostgreSQL schema migrations (DDL)
queries/      inline .sql query files; JauntyQ generates typed C# from these
schema/       jaunty.schema.json — the committed schema snapshot JauntyQ reads
seed/         sample data for local/dev databases (not production);
              apply with `psql -f db/seed/0001_dev_seed.sql`

migrate.sh        applies migrations/ in order, tracking them in schema_migrations
test-run-lock.sh  proves migrate.sh serialises itself; run it against an
                  already-migrated database, it applies nothing
```

## Running two migrations at once

`migrate.sh` takes a PostgreSQL advisory lock for the whole run, so a second
runner against the same database announces that it is queuing and waits. Without
it both runners pass the "is this already applied?" check before either records,
and both apply the file: loud for DDL, **silent for a data migration**.

The lock is per-database, so runs against different databases do not block each
other. `bash db/test-run-lock.sh` verifies the whole behaviour in about a minute
and exits non-zero if any part of it has regressed.

## JauntyQ flow

1. Apply migrations to a local database.
2. `jaunty schema pull` writes `schema/jaunty.schema.json` from that database.
3. Write queries as `.sql` files under `queries/` (auto-CRUD needs no file).
4. The JauntyQ source generator turns `queries/*.sql` + the schema snapshot into
   typed C# at build time in `apps/api`.

Legacy reference: the old app's ~80 stored procedures live in
`99-archives/2.-/.../db`. They are being translated to inline `.sql` here (no
stored procedures in the rewrite).

## Phase 1

The Phase 1 migrations (0002–0012) port the legacy SQL Server tables to
PostgreSQL. Notable deviations from the legacy schema:

- The legacy `Errors` table is NOT ported; the rewrite uses structured logging.
- Legacy `value_encrypted VARBINARY(128)` columns are dropped (unused remnants).
- Tokens, codes, and secrets are hashed at rest (`token_hash` / `secret_hash` /
  `code_hash`, CHAR(64) SHA-256 hex); no plaintext token material is stored.
- Refresh tokens gain rotation columns (`rotated_at`, `replaced_by_id`) and a
  mandatory `expires_at` (legacy refresh tokens never expired).
