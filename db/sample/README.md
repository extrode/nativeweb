# db/sample

The scaffolding demo's schema, kept apart from the identity schema next door.

```
migrations/  0001_articles.sql -- the demo's one table
queries/     inline .sql the JauntyQ generator turns into typed C#
schema/      sample.schema.json -- the committed snapshot, from `jaunty schema pull`
```

## Why this is not in `db/migrations`

`db/` proper is epass.io's identity schema, and `db/schema/jaunty.schema.json` is
its committed snapshot. A demo `articles` table added there would ship to
production and turn up in the generated data layer of every consumer of
`libs/nativeweb.data`. The demo owns its own database, its own migration and its
own snapshot instead, and `apps/sample` hosts the generator itself rather than
referencing `nativeweb.data`.

That separation is also the more honest demonstration: it is exactly what a
product stamped from this template does with its own tables.

## Setting up locally

Two databases, both owned by `nativeweb_app`:

```bash
# the one `dotnet run` uses when you configure a connection string
psql -U postgres -c "CREATE DATABASE nativeweb_sample_dev OWNER nativeweb_app"
psql -U nativeweb_app -d nativeweb_sample_dev -f db/sample/migrations/0001_articles.sql

# the one the integration and e2e tiers use
psql -U postgres -c "CREATE DATABASE nativeweb_sample_test OWNER nativeweb_app"
psql -U nativeweb_app -d nativeweb_sample_test -f db/sample/migrations/0001_articles.sql
```

Re-pull the snapshot after any change to `migrations/`:

```bash
JAUNTYQ_CONNECTION="Host=127.0.0.1;Database=nativeweb_sample_dev;Username=nativeweb_app" \
  <jauntyq>/src/JauntyQ.Cli/bin/Release/net8.0/JauntyQ.Cli.exe \
  schema pull --provider postgres --output db/sample/schema/sample.schema.json
```

`--output` must be relative and inside the current directory; the CLI rejects a
rooted path.

## The tests do not truncate

Both tiers mint a unique title per test and assert only on rows they created, so
`nativeweb_sample_test` accumulates rows across runs. That is deliberate — a
shared TRUNCATE is what breaks the moment two runs overlap — and it has one
visible consequence: a newly created row is not on page one of the scaffolded
list, so browser tests filter before they click. Drop and recreate the database
if it ever gets large enough to slow the suite.

**The public feed's tests are the one exception, and not a shared TRUNCATE.**
`GET /articles` shows the newest twenty, so those tests date their rows into the
future to be certain they are on the page — and a future-dated row left behind
would displace the *next* run's. Each one deletes exactly the ids it minted, by
prefix, in a `finally`. Two overlapping runs still cannot collide.

## Selecting the store

`apps/sample` reads `ConnectionStrings:Sample`. Unset selects the in-memory
store, which is why the demo still runs, and the whole scaffolding test suite
still passes, with no PostgreSQL at all.
