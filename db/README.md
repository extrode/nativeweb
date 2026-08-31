# db

```
sample/           the scaffolding demo's schema and queries; see sample/README.md
migrate.sh        UNREACHABLE -- reference copy; the live one is skeleton/db/migrate.sh
test-run-lock.sh  UNREACHABLE -- drives migrate.sh, so it aborts with it
```

**`sample/` is the only schema this repository has.** `migrations/`, `queries/`, `schema/` and
`seed/` used to sit beside it and held an identity provider's tables — users, realms, tokens,
authenticator devices — feeding `libs/nativeweb.data`. All of it left on 2026-08-31 with the
provider itself; see `docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`. A framework does not
ship a schema, so nothing replaced them, and a new top-level schema arriving here is a scope
question rather than a migration.

**`migrate.sh` was retargeted on 2026-08-31.** It used to resolve `db/migrations/`, which went with
the provider, and to name `0002_extensions.sql` unconditionally, so every run aborted on the missing
file and `test-run-lock.sh` aborted with it. It now defaults to `db/sample/migrations/`, takes
`MIGRATIONS_DIR` for anything else, refuses a missing or migration-less directory, and applies the
superuser extensions step only if the schema has one. `scripts/deploy/deploy.sh` names
`MIGRATIONS_DIR` explicitly rather than inheriting that default, so a product deploy cannot apply
this demo schema by accident. A stamped product gets `skeleton/db/migrate.sh`, which has its own
`__PRODUCT__` text and carries the same three guards.

## Running two migrations at once

`migrate.sh` takes a PostgreSQL advisory lock for the whole run, so a second runner against the
same database announces that it is queuing and waits. Without it both runners pass the "is this
already applied?" check before either records, and both apply the file: loud for DDL, **silent for
a data migration**.

The lock is per-database, so runs against different databases do not block each other.
`bash db/test-run-lock.sh` used to verify the whole behaviour in about a minute; it cannot run
against this copy any more, for the reason above. The behaviour it covered is in
`skeleton/db/migrate.sh` as well, where a stamped product can exercise it.

## JauntyQ flow

1. Apply migrations to a local database.
2. `jaunty schema pull` writes a schema snapshot JSON from that database, into the schema
   directory beside the migrations — `db/sample/schema/` here.
3. Write queries as `.sql` files under the queries directory (auto-CRUD needs no file).
4. The JauntyQ source generator turns `queries/*.sql` plus the schema snapshot into typed C# at
   build time, in whichever project declares them as `AdditionalFiles`.

`db/sample/` is that flow worked through end to end at the smallest scale that proves it, and
`apps/sample/nativeweb.sample.csproj` is the project that consumes it.
