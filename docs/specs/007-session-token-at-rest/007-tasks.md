# 007 — Tasks
Plan: ./007-plan.md
Spec: ./007-spec.md

- [x] T1 `SessionToken` choke-point type — files: `libs/nativeweb.auth/SessionToken.cs`,
  `tests/nativeweb.web.tests.unit/SessionTokenTests.cs` — covers: R1, R2, R4 — done when:
  the type exposes only `Mint`, `TryFromCookie`, `CookieValue` and `StorageId`, production
  cannot obtain the plaintext as a `Guid`, and a unit test pins the parity vector
  `7f3d1c2e-0a4b-4c8d-9e1f-2a3b4c5d6e7f` → `d9b9a830-04e8-1a1f-88d9-5ceec05b38d2`.

- [x] T2 Migration 0054 — files: `db/migrations/0054_bff_store_session_id_hashed.sql` — covers:
  R6, R7 — done when: the `UPDATE … WHERE kind = 'session'` runs clean against a populated
  `nativeweb_dev`, and the header states the `kind` restriction and the one-way rollback
  behaviour.

- [x] T3 Mint and read paths — files: `apps/web/Auth/SessionAuth.cs`,
  `apps/web/Auth/SessionAuthenticationHandler.cs` — covers: R1, R3 — done when: the insert
  writes `StorageId`, the cookie carries `CookieValue`, and lookup is still one point lookup
  on the primary key.

- [x] T4 Sign-out and revoke-others — files: `apps/web/Auth/SignInEndpoints.cs`,
  `apps/web/Auth/SettingsEndpoints.cs`, `apps/web/Auth/PassphraseEndpoints.cs` — covers: R5 —
  done when: no `Guid.TryParse`/`Guid.Parse` of a session cookie remains anywhere, and the
  `r.Id == currentSessionId` comparison in `LoadSessionsAsync` compares against `StorageId`.

- [x] T5 Test-database seams — files: `tests/nativeweb.web.tests.integration/TestDatabase.cs`,
  `tests/nativeweb.web.tests.e2e/TestDatabase.cs` — covers: R9 — done when: both helpers hash
  before insert/delete and still return the plaintext token, and the ~88 existing callers
  compile and pass untouched.

- [x] T6 Assertions against the real table — files:
  `tests/nativeweb.web.tests.integration/SessionAuthTests.cs` — covers: R1, R5, R6, R8 — done
  when: a test signs in, reads `bff_store` directly and asserts no row's `id` equals the cookie;
  presenting the stored `id` as a cookie is unauthenticated; the SQL expression equals the C#
  `StorageId`; three-session revoke-others behaves as before. Perturbation confirmed: removing
  the hash from the write path fails it.

- [x] T7 End-to-end — files: `tests/nativeweb.web.tests.e2e/TestDatabase.cs`,
  `tests/nativeweb.web.tests.e2e/SettingsFlowTests.cs` — covers: R9 — done when: a real browser
  signs in, navigates authenticated, and signs out. The revoke-others flow additionally
  re-navigates to `/settings` after the revoke, so the "current session survives" half is capable
  of failing — the browser cookie jar keeps the cookie whatever the server deleted.

- [x] T8 Close-out — files: `CHANGELOG.md`, `work/status/*`, `work/todo.md` — covers: R10 —
  done when: the `bff_store` plaintext item is struck from `todo.md`'s V6 review list, the
  suites are green at or above 374/247/59, `dotnet publish apps/web -c Release` is zero-warning
  and within budget, and `structure-audit.js` reports clean.

## Coverage check

| R | Task |
|---|---|
| R1 no recoverable value | T1, T3, T6 |
| R2 ≥120-bit CSPRNG | T1 |
| R3 single point lookup | T3 |
| R4 single choke point (partial — see spec) | T1 |
| R5 revoke paths unchanged | T4, T6 |
| R6 in-place migration | T2, T6 |
| R7 rollback stated | T2 |
| R8 regression test vs the table | T6 |
| R9 three tiers | T5, T6, T7 |
| R10 clean AOT publish | T8 |

No orphans: every task traces to a plan section, every requirement has at least one task.
