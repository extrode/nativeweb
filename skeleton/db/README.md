# db

Database assets for __PRODUCT__ (`apps/__product__`): its own database (`__product___dev`),
its own app role (`__product___app`), its own migration history.

```
migrations/   ordered PostgreSQL schema migrations (DDL); 0001 needs superuser (citext)
queries/      inline .sql query files; JauntyQ generates typed C# from these
schema/       __product__.schema.json — the committed schema snapshot JauntyQ reads
migrate.sh    applies migrations in order, tracked in schema_migrations
```

## One-time local provisioning (superuser)

```sql
CREATE ROLE __product___app LOGIN PASSWORD '...';
CREATE DATABASE __product___dev OWNER __product___app;
```

Then `./migrate.sh`. CI provisions `__product___test` the same way in the workflow.

## JauntyQ flow

1. Apply migrations to a local database (`./migrate.sh`).
2. `jaunty schema pull` writes `schema/__product__.schema.json` from that database.
3. Write queries as `.sql` files under `queries/<Entity>/` (auto-CRUD needs no file).
4. The generator (hosted in `libs/__product__.data`) turns queries + snapshot into
   typed C# at build time.
