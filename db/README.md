# db

```
sample/           the scaffolding demo's schema and queries; see sample/README.md
migrate.sh        applies a migrations/ directory in order, tracking them in schema_migrations
test-run-lock.sh  proves migrate.sh serialises itself; run it against an
                  already-migrated database, it applies nothing
```

**`sample/` is the only schema this repository has.** `migrations/`, `queries/`, `schema/` and
`seed/` used to sit beside it and held an identity provider's tables — users, realms, tokens,
authenticator devices — feeding `libs/nativeweb.data`. All of it left on 2026-08-31 with the
provider itself; see `docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`. A framework does not
ship a schema, so nothing replaced them, and a new top-level schema arriving here is a scope
question rather than a migration.

`migrate.sh` and `test-run-lock.sh` stay because they are schema-agnostic tooling: `scripts/deploy/`
runs the first against whatever schema a stamped product has, and the skeleton inherits the shape.

## Running two migrations at once

`migrate.sh` takes a PostgreSQL advisory lock for the whole run, so a second runner against the
same database announces that it is queuing and waits. Without it both runners pass the "is this
already applied?" check before either records, and both apply the file: loud for DDL, **silent for
a data migration**.

The lock is per-database, so runs against different databases do not block each other.
`bash db/test-run-lock.sh` verifies the whole behaviour in about a minute and exits non-zero if any
part of it has regressed.

## JauntyQ flow

1. Apply migrations to a local database.
2. `jaunty schema pull` writes a `schema/jaunty.schema.json` from that database.
3. Write queries as `.sql` files under `queries/` (auto-CRUD needs no file).
4. The JauntyQ source generator turns `queries/*.sql` plus the schema snapshot into typed C# at
   build time, in whichever project declares them as `AdditionalFiles`.

`db/sample/` is that flow worked through end to end at the smallest scale that proves it, and
`apps/sample/nativeweb.sample.csproj` is the project that consumes it.
