# db

```text
sample/           the scaffolding demo's schema and queries; see sample/README.md
migrate.sh        applies a directory of NNNN_*.sql migrations in order
test-run-lock.sh  checks that migrate.sh runs one at a time
```

`sample/` is the only schema this repository has. A framework does not ship a schema, so a new
top-level schema here is a scope question, not a migration. The reasoning is in
[the decision record](../docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md).

## migrate.sh

It applies `NNNN_*.sql` files in order and records each applied version in `schema_migrations`,
so a re-run skips what is already there. Each file and its `schema_migrations` row commit in one
transaction. Files that cannot run inside a transaction (`CREATE INDEX CONCURRENTLY`) are detected
by content and run on their own.

- It applies `db/sample/migrations/` by default. Set `MIGRATIONS_DIR` to apply another directory.
- It stops if the directory is missing or holds no `NNNN_*.sql` file.
- It runs the superuser step (`NNNN_extensions.sql`) only if the directory has one. A schema that
  needs no extension needs no superuser.
- `PGHOST`, `PGPORT`, `PGDATABASE`, `APP_ROLE` and `SUPERUSER_ROLE` override the local dev
  defaults; the header of the script lists them.

`scripts/deploy/deploy.sh` names `MIGRATIONS_DIR` explicitly, so a product deploy cannot apply the
demo schema by accident. A stamped product gets its own copy, `skeleton/db/migrate.sh`, derived
from this one.

## Running two migrations at once

`migrate.sh` takes a PostgreSQL advisory lock for the whole run, so a second runner against the
same database says it is queuing and waits. Without the lock both runners pass the "is this
already applied?" check before either records, and both apply the file: loud for DDL, **silent for
a data migration**.

The lock is per-database, so runs against different databases do not block each other.
`bash db/test-run-lock.sh` verifies this against a database that is already fully migrated. It
applies nothing.

## JauntyQ flow

1. Apply migrations to a local database.
2. `jaunty schema pull` writes a schema snapshot JSON from that database, into the schema
   directory beside the migrations: `db/sample/schema/` here.
3. Write queries as `.sql` files under the queries directory (auto-CRUD needs no file).
4. The JauntyQ source generator turns `queries/*.sql` plus the schema snapshot into typed C# at
   build time, in whichever project declares them as `AdditionalFiles`.

`db/sample/` is that flow worked through end to end at the smallest scale that proves it, and
`apps/sample/nativeweb.sample.csproj` is the project that consumes it.
