# 007 — Plan
Spec: ./007-spec.md
Approved: 2026-08-16

Covers R1–R10. Approved in plan mode and promoted here unchanged.

## Context

`apps/web/Auth/SessionAuth.cs:62-68` mints a session by generating a GUID, writing it as the
`bff_store` primary key, and handing that same value to the browser as the
`__Host-NativeWeb.Session` cookie. The token is unguessable, but it is not secret at rest:
anything that can read the table — a backup, a replica, a slow-query log, a read-only injection
primitive — yields live bearer credentials good for fourteen days. Every other credential in the
system is already hashed or protected (`OneTimeCodes.Hash`, Argon2id, `storeProtector.Protect`),
and the sibling product from the same template stores only a SHA-256 of its token. This is the
one exception, and it is the highest-value one. It is free to fix now and expensive after the
epass.io cutover, when the plaintext is in every backup ever taken.

## Design

**Store `SHA-256(cookie value)` truncated to 128 bits, keeping the column a `UUID`.**

Rejected: widening `id` to `BYTEA`. It is the conventional shape, but it changes the column
type, all eight `db/queries/BffStore/*.sql`, the JauntyQ-generated types, and the `Guid`
parameter of every call site — for 128 extra bits that buy nothing. The token itself carries
122 bits of entropy (a v4 GUID fixes six for version and variant; this plan originally said 128
and was wrong), so 128-bit preimage resistance already exceeds the thing it protects. The
truncated hash is not a valid RFC 4122 UUID (random version/variant bits); Postgres does not
care, and the migration says so.

**Verified, not assumed.** These two agree exactly, checked against the live dev database and
`sha256sum` before writing this plan — input `7f3d1c2e-0a4b-4c8d-9e1f-2a3b4c5d6e7f` gives
`d9b9a830-04e8-1a1f-88d9-5ceec05b38d2` on both sides:

- SQL: `encode(substring(sha256(id::text::bytea) from 1 for 16), 'hex')::uuid`
- C#: SHA-256 over UTF-8 of the canonical lowercase `"d"` format, first 16 bytes

**The C# trap:** `new Guid(byte[])` reads the first three fields little-endian, so the naive
construction does *not* match the SQL. It must be `new Guid(hash.AsSpan(0, 16), bigEndian: true)`.
A unit test pins that exact input/output pair — it is the single assertion holding the two
derivations together, and it is the highest-value test in the change.

### `libs/nativeweb.auth/SessionToken.cs` (new) — the choke point

*Built at `libs/nativeweb.auth/`, not the `apps/web/Auth/` this plan first named: the e2e project
has no `InternalsVisibleTo` from `apps/web` but already references `NativeWeb.Auth`, and the
alternative was a second copy of the derivation in the e2e test helper.*

A `readonly struct` holding the plaintext privately and exposing only:

- `static SessionToken Mint()` — `Guid.NewGuid()` (already CSPRNG on both platforms).
- `static bool TryFromCookie(string?, out SessionToken)` — replaces the four scattered
  `Guid.TryParse(cookieValue, …)` calls.
- `string CookieValue` — for `Set-Cookie` only.
- `Guid StorageId` — the hash; the only value the data layer ever receives.

R4 is a speed bump, not construction — corrected after review. Production code cannot obtain the
plaintext *as a `Guid`* by asking the type for one, so it will not reach `db.BffStore.*Async` by
accident; but `Guid.Parse(token.CookieValue)` is one hop away and would compile. Meeting R4
properly needs the data layer's session key to be its own type instead of `Guid`, which is a
JauntyQ-wide change and out of scope here; the gap is recorded in the spec and in `work/todo.md`.
`ToString()` *is* overridden, to a redacted constant — the plaintext must not be one careless
interpolation away from a log.

### Call sites — four reads, one write

All four cookie→`Guid` parses become `SessionToken.TryFromCookie`, and every `db.BffStore` call
takes `.StorageId`:

| File | Change |
|---|---|
| `Auth/SessionAuth.cs:60-69` | `MintSessionAsync` inserts `StorageId`, sets `CookieValue` |
| `Auth/SessionAuthenticationHandler.cs:32-53` | parse → `GetByIdAsync(token.StorageId, …)` |
| `Auth/SignInEndpoints.cs:431-437` | sign-out `DeleteByIdAsync(token.StorageId, …)` |
| `Auth/SettingsEndpoints.cs:339,356-360` | revoke-others + the `r.Id == currentSessionId` compare |
| `Auth/PassphraseEndpoints.cs:636-639` | revoke-others after a passphrase change |

`SettingsPage.SessionRow.Id` is never rendered (`Render/SettingsPage.cs:244-255` uses only
`IsCurrent` and the dates), so there is no UI change and no new value exposed to the page.

### `db/migrations/0054_bff_store_session_id_hashed.sql` (new)

```sql
UPDATE bff_store
SET id = encode(substring(sha256(id::text::bytea) from 1 for 16), 'hex')::uuid
WHERE kind = 'session';
```

`WHERE kind = 'session'` deliberately: the register/reset/TOTP/authorize rows are keyed by
`temp_codes.unique_id` and reached via the username — their id is *not* a client-held bearer
token (the emailed code is, and it is already hashed). Hashing them would break those lookups
for no security gain. The header comment records that, and R7's rollback note: an old binary
against the new schema signs everyone out but serves traffic normally — non-destructive, but
one-way, since the plaintext cannot be recovered from the hash.

### Test seam — no churn at 88 call sites

`TestDatabase.InsertSessionAsync` (integration `:31-43`, and the e2e copy `:56-69`) keeps
returning the **plaintext** `Guid` and hashes internally before the insert; `DeleteSessionAsync`
hashes before the delete. Its ~88 callers across nine integration files and `PassphraseFlowTests`
use the return value only to build a cookie or to delete, so they are untouched — the helper is
the one seam that has to change.

## Tasks

| # | Task | Files |
|---|---|---|
| T1 | `SessionToken` + unit tests, incl. the pinned SQL-parity vector | `libs/nativeweb.auth/SessionToken.cs`, `tests/…unit/SessionTokenTests.cs` |
| T2 | Migration 0054 + a test that the SQL expression equals `StorageId` | `db/migrations/0054_…sql`, `tests/…integration/` |
| T3 | Mint + read paths | `SessionAuth.cs`, `SessionAuthenticationHandler.cs` |
| T4 | Sign-out and the two revoke-others paths | `SignInEndpoints.cs`, `SettingsEndpoints.cs`, `PassphraseEndpoints.cs` |
| T5 | Test-database seams | both `TestDatabase.cs` |
| T6 | R8 assertion against the actual table + R5 revoke coverage | `tests/…integration/SessionAuthTests.cs` |
| T7 | e2e session survives sign-in → navigate → sign-out | `tests/…e2e/` |

T1 first (everything depends on the type). T2 is independent. T3–T5 must land together or the
suites are red between them. T6–T7 last.

## Verification

- **Unit:** the pinned parity vector; `StorageId != CookieValue`; determinism; `TryFromCookie`
  rejects non-GUID input; `Mint` produces distinct values.
- **Integration (R8, the one that matters):** sign in through the real endpoint, take the
  `Set-Cookie` value, then `SELECT id FROM bff_store WHERE kind='session'` and assert **no row's
  id equals it** — asserted against the table, not against the helper. Then present the stored
  id as the cookie and assert the request is unauthenticated. Plus R5: three sessions, revoke
  others, current survives, list shows one.
- **Integration (R6):** insert a row with a known plaintext id, run the migration's expression,
  assert it equals the C# `StorageId` — this is what would silently break the backfill.
- **e2e:** browser sign-in, authenticated navigation, sign-out.
- **Perturbation, per the testing rule:** remove the hashing from the write path and confirm the
  R8 test fails; flip `bigEndian: true` to the default and confirm the parity vector fails. A
  green new test is not evidence until it has been made to fail.
- **Whole suite + publish:** unit ≥ 374, integration ≥ 247, e2e ≥ 59; `dotnet publish apps/web
  -c Release` zero warnings, within budget; `structure-audit.js` clean.

## Out of scope

`bff_store.data` encryption; the non-session flow rows; token rotation on privilege change; the
skeleton (ships no auth); manzil.my (already hashes). All recorded in the spec's Non-goals.
