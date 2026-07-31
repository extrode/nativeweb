# 002 — IdP hardening: tasks

Task breakdown for `002-plan.md`. One task per branch; each ships code + tests, gets a Fable
review, and merges to `dev` with `--no-ff` before the next starts. Order is H1→H7 (small fixes
first; the role rename last because it touches the local DB and CI environment everything else
runs against). Migration numbers allocated centrally: 0049 + 0050 in H5, 0051 in H6.

- [ ] **H1** `fix/e2e-appprocess-fixture` — port the DevMarks fixture fixes to
  `tests/nativeweb.web.tests.e2e/AppProcessFixture.cs`: dispose `_process` on the
  InitializeAsync-timeout path; derive the RID instead of hardcoding `win-x64`. (R12)
- [ ] **H2** `fix/idp-csp-htmx-indicator` — `htmx-config` meta (`includeIndicatorStyles:false`)
  in the layout head, `.htmx-indicator` rules in `components.css`, widen the e2e CSP assertion
  back to `Assert.Empty(violations)`. (R9)
- [ ] **H3** `fix/idp-oauth-rfc` — percent-decode Basic client credentials; reject
  header+body dual credentials; introspection without `token` → 400; auth-code and device-code
  grants re-check the user is active before minting. (R10)
- [ ] **H4** `feat/idp-admin-unflag` — stateless clear form on `/admin/accounts` →
  `/admin/accounts/clear-passphrase-reset`, `ClearMustResetPassphrase.sql`, indistinguishable
  responses, three-tier tests. (R7)
- [ ] **H5** `feat/idp-reset-hardening` — email-or-username reset identifier; per-account
  issuance cap (migration 0050 + config `NativeWeb:PassphraseReset:MaxCodesPerDay`); atomic
  success writes via `BeginTransactionAsync`; `bff_store.user_id` backfill (migration 0049);
  full IANA TLD list in `FakeIdentifierResponses`; step-up UV=Required regression test.
  (R1–R6)
- [ ] **H6** `feat/idp-breach-throttle` — migration 0051 `users.last_breach_check_at`; HIBP
  network leg only outside `NativeWeb:BreachCheck:RecheckAfterHours` (default 24); offline list
  still every sign-in; kill-switch unchanged. (R8)
- [ ] **H7** `chore/db-role-rename` — `nativeweb_app`: `db/migrate.sh` default, `ci.yml`
  (POSTGRES_USER, conn strings, 0001 special case, rationale comment), local
  `ALTER ROLE epass_app RENAME TO nativeweb_app` + conn-string updates. (R11)

Close-out: all three suites green on `dev`; zero-warning Release AOT publish of `apps/web`;
screenshot check of the admin and reset pages; CHANGELOG entry; archive the tasklist pointer.
