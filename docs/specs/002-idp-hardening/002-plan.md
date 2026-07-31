# IdP hardening (spec 002) — technical plan

> Plan for `docs/specs/002-idp-hardening/002-spec.md`. Approved 2026-07-31; promoted here from
> the plan-mode scratch file and committed in the first branch's first commit.

## Context

The IdP (`apps/web`) shipped its v2 redesign with review findings deliberately deferred. Spec 002
closes them before the production cutover. User decisions (2026-07-31): reset accepts **email or
username**; admin un-flag is a **stateless form**; the **`epass_app` role rename is in scope**.

Verified against code first — the backlog list was partially stale:
- **Already fixed, verify only:** WebAuthn step-up UV=Required (`WebAuthnAuth.cs:48-51` with
  rationale comment; `PassphraseEndpoints.cs:403` uses it). Add a regression test if none asserts it.
- **Lighter than feared:** no migration file references `epass_app` — only `db/migrate.sh:28`
  (default `APP_ROLE`) and `.github/workflows/ci.yml` (POSTGRES_USER, conn strings, 0001 special
  case) hardcode it. Rename = config edits + local `ALTER ROLE ... RENAME` (SCRAM passwords
  survive a rename; only MD5 ones are username-salted).
- **Backfill is trivial:** `bff_store.data` for `kind='session'` is `{"UserId":n}` JSON
  (`SessionAuth.cs:56`), so migration = one UPDATE.

## Work items (grouped into branches)

### H1 `fix/e2e-appprocess-fixture` — R12
`tests/nativeweb.web.tests.e2e/AppProcessFixture.cs`: port the two fixes already made in the
DevMarks copy (`tests/nativeweb.devmarks.tests.e2e/AppProcessFixture.cs`): dispose `_process` on
the InitializeAsync-timeout path; derive the RID instead of hardcoding `win-x64`.

### H2 `fix/idp-csp-htmx-indicator` — R9
- `apps/web/Render/Layout.cs` head: add `<meta name="htmx-config" content='{"includeIndicatorStyles":false}'>`
  (htmx reads the meta before injecting its inline `<style>`, which our
  `style-src 'self' 'nonce-{n}'` CSP blocks on every page load today).
- Add the `.htmx-indicator` rules to `apps/web/wwwroot/css/components.css` so indicators work the
  day someone uses one.
- e2e: widen `Enroll_ReachedByHtmxSwap_RunsTheCeremonyWithoutACspViolation` back to
  `Assert.Empty(violations)` (currently filtered to script-* to dodge this noise).

### H3 `fix/idp-oauth-rfc` — R10
`apps/web/Auth/OAuthTokenEndpoints.cs`:
- `TryDecodeBasic` (line ~389): percent-decode id/secret per RFC 6749 §2.3.1
  (`Uri.UnescapeDataString` after the `:` split).
- `ReadClientCredentials` (~line 361): today Basic "wins if present" — instead reject requests
  presenting credentials in both header and form with `invalid_request`.
- Introspection: missing `token` parameter → 400 `invalid_request` (RFC 7662 §2.1), not
  200 `{"active":false}`.
- Auth-code and device-code grants: re-check the user is still active before minting (mirror the
  login-link grant's disabled check; existing `db.Users.GetById` + `disabled_at` filter pattern).

### H4 `feat/idp-admin-unflag` — R7
`apps/web/Auth/AdminEndpoints.cs` (flag form at ~line 140-180) + admin page render:
- New query `db/queries/Users/ClearMustResetPassphrase.sql`
  (`UPDATE users SET must_reset_passphrase_at = NULL WHERE id = @id`).
- Second stateless form on `/admin/accounts` posting to `/admin/accounts/clear-passphrase-reset`,
  same (realm, username) lookup, same indistinguishable-response discipline as the setter
  (unknown username and unflagged account respond identically to success).
- Tests in all three tiers, mirroring the setter's coverage.

### H5 `feat/idp-reset-hardening` — R1–R6 (reset flow + related)
All in `apps/web/Auth/PassphraseEndpoints.cs` + `FakeIdentifierResponses.cs` + migrations:
- **R1 email identifier:** in `PostResetStart`, if the submitted identifier contains `@`,
  normalize and resolve via the existing `db.Users.GetByEmailAddressAsync(realmId,
  protector.EmailIndex(...))` blind-index lookup (pattern at `OAuthLoginLinkEndpoints.cs:144`);
  else the current username path. Throttle/verify keys stay on the hash of the *submitted*
  identifier so real/fake/either-kind responses remain indistinguishable; `temp_codes` keeps the
  resolved username. Update `PassphraseResetPage` copy ("username or email").
- **R2 per-account issuance cap:** migration **0050** adds a rolling-window pair to
  `login_attempts` (e.g. `codes_sent_count`, `codes_window_started_at`); cap issuance (default
  5/24 h, config `NativeWeb:PassphraseReset:MaxCodesPerDay`) on top of the existing 30 s
  `ResendFloodWindow`. Keyed on the same hashed-identifier row real and fake requests share, so
  the cap itself leaks nothing.
- **R3 atomic reset success:** wrap the six success writes in `PostResetVerify` (temp-code
  delete → `UpdatePassphraseFull` → lock clears → `BffStore.DeleteAllByUserId`) in the existing
  `db.BeginTransactionAsync` pattern (as `OAuthTokenEndpoints.cs:165` does).
- **R4 session backfill:** migration **0049**:
  `UPDATE bff_store SET user_id = (data::jsonb->>'UserId')::bigint WHERE kind = 'session' AND user_id IS NULL`.
- **R5 TLD oracle:** replace `FakeIdentifierResponses.EmailTlds` (5 entries) with the full IANA
  TLD list vendored as a generated static array, hash-indexed as today — no real-world domain
  ending falls outside the synthetic set, so mask endings no longer separate real from fake.
- **R6:** regression test asserting the step-up assertion options carry
  `UserVerificationRequirement.Required` (add only if no existing test pins it).

### H6 `feat/idp-breach-throttle` — R8
- Migration **0051**: `users.last_breach_check_at TIMESTAMPTZ NULL` + queries to read/stamp it.
- `SignInEndpoints.cs` (`CheckPassphraseSafelyAsync` call site ~line 269): offline
  common-passphrase list still runs every sign-in (free); the HIBP network leg runs only when
  `last_breach_check_at` is NULL or older than `NativeWeb:BreachCheck:RecheckAfterHours`
  (default 24). Stamp on a completed network check. `NativeWeb:BreachCheck:Hibp=false`
  kill-switch unchanged.

### H7 `chore/db-role-rename` — R11
New role name: **`nativeweb_app`**.
- `db/migrate.sh`: default `APP_ROLE` → `nativeweb_app`.
- `.github/workflows/ci.yml`: `POSTGRES_USER`, both conn strings, the 0001-extensions special
  case, and rewrite the line-26 "survives on purpose" comment to record the rename.
- Local dev: `ALTER ROLE epass_app RENAME TO nativeweb_app` on the local cluster + update local
  appsettings/user-secrets conn strings (SCRAM password survives).
- No migration file changes (none reference the role). Acceptance: fresh CI run green with only
  the neutral role.

## Migration numbers
Current max is 0048. Allocated centrally: **0049** backfill (H5), **0050** login_attempts cap
(H5), **0051** last_breach_check_at (H6). H5 merges before H6.

## Order & flow
Each branch off `dev`, code + tests, Fable review, merge `--no-ff`, sequentially:
H1 → H2 → H3 → H4 → H5 → H6 → H7 (small fixes first; role rename last since it touches the local
DB and CI env everything else runs against). Sequential, single working tree — the items are
small and several touch the same files (`PassphraseEndpoints.cs`, ci.yml); worktree fan-out buys
nothing here.

## Testing & verification
- Per repo rule: every branch ships unit + integration coverage; e2e where a flow changes (reset
  via email, admin un-flag, CSP assertion).
- End state: all three suites green on dev; `dotnet publish apps/web -c Release` zero
  AOT/trim warnings (`JauntyQRoot` env var set on every dotnet command); screenshot check of the
  admin page (new form) and reset page (new copy).
- R2/R8 integration tests drive the window via config overrides (env-var static-ctor pattern the
  test factories already use).
- After H7: rename the local role and run the full suite against it; the CI run is the
  acceptance for the CI leg.

## Out of scope
No admin roster, no DevMarks changes, no CSRF restyle, no vendored-migration edits, no UI
redesign. `work/todo.md` keeps the rest.
