# 002 — IdP hardening: tasks

Task breakdown for `002-plan.md`. One task per branch; each ships code + tests, gets a Fable
review, and merges to `dev` with `--no-ff` before the next starts. Order is H1→H7 (small fixes
first; the role rename last because it touches the local DB and CI environment everything else
runs against). Migration numbers allocated centrally: 0049 + 0050 in H5, 0051 in H6.

- [x] **H1** `fix/e2e-appprocess-fixture` — port the DevMarks fixture fixes to
  `tests/nativeweb.web.tests.e2e/AppProcessFixture.cs`: dispose `_process` on the
  InitializeAsync-timeout path; derive the RID instead of hardcoding `win-x64`. (R12)
- [x] **H2** `fix/idp-csp-htmx-indicator` — `htmx-config` meta (`includeIndicatorStyles:false`)
  in the layout head, `.htmx-indicator` rules in `components.css`, widen the e2e CSP assertion
  back to `Assert.Empty(violations)`. (R9)
- [x] **H3** `fix/idp-oauth-rfc` — percent-decode Basic client credentials; reject
  header+body dual credentials; introspection without `token` → 400; auth-code and device-code
  grants re-check the user is active before minting. (R10)
- [x] **H4** `feat/idp-admin-unflag` — stateless clear form on `/admin/accounts` →
  `/admin/accounts/clear-passphrase-reset`, `ClearMustResetPassphrase.sql`, three-tier tests.
  (R7 — as built: explicit operator feedback mirroring the setter, not indistinguishable
  responses; the admin surface is operator-only, so the spec was amended.)
- [x] **H5** `feat/idp-reset-hardening` — email-or-username reset identifier; per-account
  issuance cap (migration 0050 + config `NativeWeb:PassphraseReset:MaxCodesPerDay`); atomic
  success writes via `BeginTransactionAsync`; `bff_store.user_id` backfill (migration 0049);
  full IANA TLD list in `FakeIdentifierResponses`; step-up UV=Required regression test.
  (R1–R6 — as built after review: the cap is per submitted identifier (not per account, which
  would need a row fakes can't share) and floor+cap are one conditional upsert; contact-shaped
  identifiers mask as themselves; fake-branch burn queries use sentinel targets; R6 was already
  pinned by `WebAuthnUserVerificationTests`.)
- [x] **H6** `feat/idp-breach-throttle` — migration 0051 `users.last_breach_check_at`; HIBP
  network leg only outside `NativeWeb:BreachCheck:RecheckAfterHours` (default 24); offline list
  still every sign-in; kill-switch unchanged. (R8 — as built: `BreachResult.NetworkChecked`
  marks completed network checks, so the stamp is never written by an offline-only pass,
  including under the kill-switch.)
- [x] **H7** `chore/db-role-rename` — `nativeweb_app`: `db/migrate.sh` default, `ci.yml`
  (POSTGRES_USER, conn strings, 0001 special case, rationale comment), local role +
  conn-string updates. (R11 — as built: created `nativeweb_app` and `REASSIGN OWNED` of
  `nativeweb_dev` instead of the planned cluster-wide `ALTER ROLE RENAME`, because
  `epass_app` also owns other projects' local databases; renaming it would have
  broken both. `epass_app` itself is untouched and no longer referenced by this repo.)

Close-out: all three suites green on `dev`; zero-warning Release AOT publish of `apps/web`;
screenshot check of the admin and reset pages; CHANGELOG entry; archive the tasklist pointer.
