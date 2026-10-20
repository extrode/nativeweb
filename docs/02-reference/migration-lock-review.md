# Migration lock duration: review before pointing `migrate.sh` at a live database

`db/migrate.sh` wraps each migration file in one transaction. That is right for atomicity and
costs lock duration: every lock a file takes is held until its last statement commits, where
autocommit would release them statement by statement. Against a freshly provisioned database that
is free. Against a database serving traffic it blocks readers and writers for the length of the
file.

The migrations in `db/sample/` run against a database being built, so nothing there needs
changing. This is the checklist for writing migrations that will run against a live database, such
as a stamped product's own `db/migrations/`.

## The rule of thumb

| Statement | Lock | Duration |
|---|---|---|
| `ADD COLUMN` with a constant default | `ACCESS EXCLUSIVE` | Instant since PG 11 — no rewrite |
| `ALTER COLUMN … TYPE` | `ACCESS EXCLUSIVE` | Full table rewrite |
| `SET NOT NULL` | `ACCESS EXCLUSIVE` | Full table scan |
| `ADD CONSTRAINT … FOREIGN KEY` | `SHARE ROW EXCLUSIVE` on both tables | Full validation scan unless `NOT VALID` |
| `CREATE INDEX` | blocks writes | Full build |
| `CREATE INDEX CONCURRENTLY` | does not block writes | Two scans, cannot be in a transaction |
| Unqualified `UPDATE` | row locks, whole table | Proportional to rows |

## Files that need care

Only wrapped files are affected. A file carrying `-- @no-transaction` is applied statement by
statement, so every `CREATE INDEX CONCURRENTLY` migration must carry the marker. Watch for:

- Several tables altered in one file. Each `ALTER TABLE` holds its lock until the file commits, so
  the tables are locked together, not one after another.
- `ADD COLUMN` then a whole-table `UPDATE` then `SET NOT NULL` then `ADD CONSTRAINT ... FOREIGN
  KEY` then `CREATE INDEX` on one table, repeated across tables. That sequence is the worst shape.
- `ALTER COLUMN ... TYPE`, which rewrites the table under `ACCESS EXCLUSIVE`.
- Non-concurrent `CREATE INDEX` on a table that takes writes.

## What to do about it

In order of preference:

1. **Provision fresh instead.** If the cutover creates a new database and imports data, none of
   this applies.
2. **Split by table.** One file per table, each with its own `@no-transaction` where it needs one.
   Each is still atomic in the sense that matters (a half-applied *table* is recoverable, a
   half-applied *schema* is not), and no two tables are locked simultaneously.
3. **`ADD CONSTRAINT … NOT VALID`, then `VALIDATE CONSTRAINT` separately.** The second takes only
   `SHARE UPDATE EXCLUSIVE` and does not block writes.
4. **Make the non-concurrent `CREATE INDEX` statements concurrent**, which moves those files to
   `@no-transaction`.
5. **Set a `lock_timeout`** for the run so a migration that cannot get its lock quickly fails
   instead of queuing behind — and ahead of — application traffic.

## Expand now, contract later

`deploy.sh` runs migrations while the previous release is still serving, so a migration must not
drop or rename anything the running version reads. See the runbook:
[deployment.md](deployment.md#migrations-run-against-the-previous-release).
