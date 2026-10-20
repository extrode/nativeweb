# Migration lock duration — review before pointing `migrate.sh` at a live database

Reviewed 2026-08-18, against `db/migrations/0001`–`0055`.

`db/migrate.sh` wraps each migration file in one transaction (2026-08-17), which is right for
atomicity and costs lock duration: every lock a file takes is now held until its last statement
commits, where autocommit released them statement by statement. Against a provisioning database
that is free — which is the only thing these migrations have ever run against. Against a database
serving traffic it blocks readers and writers for the length of the file.

**Nothing here needs changing today.** This is the list to act on if, and only if, migrations are
ever applied to a live epass.io database rather than to a freshly provisioned one.

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

## Files that would hurt

Only wrapped files are affected. A file carrying `-- @no-transaction` is applied statement by
statement exactly as before, so every `CONCURRENTLY` migration is already out of scope.

| File | Shape | Why it matters |
|---|---|---|
| **0021_realm_scoping** | 32 statements, 20 `ALTER TABLE`, 6 unqualified `UPDATE`, 4 `CREATE INDEX` | The worst by a wide margin. For each of six tables — `users`, `applications`, `temp_users`, `temp_codes`, `email_addresses`, `phone_numbers` — it runs `ADD COLUMN` → whole-table `UPDATE` → `SET NOT NULL` → `ADD CONSTRAINT … FOREIGN KEY` → `CREATE INDEX`. Wrapped, that holds `ACCESS EXCLUSIVE` on **all six tables at once** until the last statement commits, which is every table the IdP reads on a sign-in |
| **0013_cascades_and_fk_indexes** | 13 statements, 7 `ALTER TABLE` adding FK cascades, 6 non-concurrent `CREATE INDEX` | Seven tables locked together; each FK validates with a full scan and each index blocks writes for its build |
| **0029_contact_encryption** | 8 `ALTER TABLE`, two of them `ALTER COLUMN … TYPE text` | Two full table rewrites. Already `@no-transaction` for its `CONCURRENTLY` indexes, so the rewrites are at least not held open alongside them |
| **0027_totp_hardening** | `ALTER COLUMN totp_secret TYPE VARCHAR(128)` on `users` | One rewrite of the largest table |
| **0022_realm_unique_constraints** | 12 `ALTER TABLE`, but `CONCURRENTLY` throughout | Already unwrapped. Listed so it is not re-reviewed as a suspect |

## What to do if it comes to that

In order of preference:

1. **Provision fresh instead.** Every one of these was written for a database being built, not
   migrated. If the cutover creates a new database and imports data, none of this applies.
2. **Split 0021 per table.** Six files, one per table, each with its own `@no-transaction` where
   it needs one. Each still atomic in the sense that matters — a half-applied *table* is
   recoverable, a half-applied *schema* is not — and no two tables are locked simultaneously.
3. **`ADD CONSTRAINT … NOT VALID`, then `VALIDATE CONSTRAINT` separately.** The second takes only
   `SHARE UPDATE EXCLUSIVE` and does not block writes. No migration here uses `NOT VALID` today.
4. **Make the non-concurrent `CREATE INDEX` statements concurrent**, which moves those files to
   `@no-transaction`.
5. **Set a `lock_timeout`** for the run so a migration that cannot get its lock quickly fails
   instead of queuing behind — and ahead of — application traffic.

## What was checked and is fine

- Every `CONCURRENTLY` migration already carries `@no-transaction`, so the transaction wrap never
  applies to one. Verified by `MigrateRunnerTests.EveryMigrationNeedingTheMarker_CarriesIt`.
- No migration adds a column with a **volatile** default, which would force a rewrite on PG 11+.
- The data migrations (0049, 0054, 0055) touch `bff_store` only, which is session state rather
  than user data, and 0054 manages its own transaction.
