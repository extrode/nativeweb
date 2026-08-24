# 002 — IdP hardening: security & conformance backlog close-out
Status: done (H1–H7 all ticked, merged to dev 2026-07-31, archived to
`work/archive/2026-07-31-idp-hardening.md`; R7 amended at H4, R11 mechanism amended at H7 — see
tasks. The sign-off-gated production cutover replacing epass.io is downstream of this spec, not a
task in it; it is tracked in `work/status/next.md`.)
Created: 2026-07-31

## Problem
The epass.io IdP (`apps/web`) shipped its v2 redesign with a set of review findings deliberately
deferred: a reset flow that some accounts cannot complete, an operator flag with no operator
remedy, a third-party breach-check service on the hot path of every sign-in, a CSP violation on
every page load, a weakened WebAuthn step-up requirement, minor OAuth RFC deviations, a legacy
database role name, and two test-fixture defects that block cross-platform CI. Individually each
was judged non-launch-blocking; collectively they are the gap between "reviewed" and "hardened",
and they must close before the production cutover replacing epass.io.

## Goals
- Every account, including auto-provisioned passwordless ones, can complete the passphrase reset
  flow unaided.
- Operators can reverse their own mistakes: a forced-reset flag can be lifted without the user
  acting.
- No third-party service is a mandatory per-sign-in dependency; breach checking still happens.
- Zero CSP violations reported by the browser on any page, asserted by the e2e suite.
- The security posture findings from the Fable reviews (#6–#8 backlog set) are closed, not
  re-deferred.
- Token/introspection endpoints conform to RFC 6749 and RFC 7662 on the flagged points.
- No `epass`-named artifacts remain in the database layer; local dev and CI both run on the
  neutral name.
- The e2e test infrastructure runs on non-Windows CI runners and leaks no process handles.

## Non-goals
- No new admin account roster or account-search UI: `/admin/accounts` stays stateless.
- No changes to the DevMarks app.
- No new IdP features, UI redesign, or Vault/Share work (closed by the 2026-07-26 decision).
- No changes to the vendored, feature-frozen legacy migration files themselves; forward
  migrations only.
- Not in scope: the CSRF markup restyle and the other `work/todo.md` "Later" items.

## User stories
- As a passwordless user who has never seen my generated username, I want to identify myself by
  email in the reset flow, so that a forced reset does not lock me out.
- As an operator who flagged the wrong account, I want to clear the forced-reset flag myself, so
  that an innocent user is not confined by my typo.
- As a signing-in user, I want sign-in latency independent of a third party, so that an outage
  elsewhere does not slow or block my login.
- As a security reviewer, I want step-up verification to require user verification, so that a
  stolen no-PIN security key plus a hijacked cookie cannot set a permanent password.
- As an integrator, I want the token endpoints to follow the RFCs, so that off-the-shelf clients
  interoperate without workarounds.
- As a maintainer, I want the e2e tier to run on Linux CI, so that the suite gates every merge.

## Requirements
- **R1** The passphrase reset flow accepts either a username or an email address as the account
  identifier. Responses for unknown, known, and mismatched identifiers are indistinguishable.
- **R2** Reset-code issuance is rate-limited per account, in addition to any per-IP limits.
- **R3** The state changes performed on a successful reset are atomic: no partial outcome is
  observable if any step fails.
- **R4** Session records created before user-id stamping was introduced carry their owning user
  id after this work; no session row remains with an unknown owner.
- **R5** The synthetic identifier masks shown for unknown accounts are not an existence oracle:
  an observer cannot distinguish real from synthetic masks by their domain endings for any
  real-world email domain.
- **R6** The step-up ceremony guarding passphrase creation on passwordless accounts requires
  user verification; a credential that cannot verify the user is rejected.
- **R7** An operator can clear a forced-reset flag via the same stateless (realm, username)
  lookup used to set it, with the same explicit feedback the setter gives (unknown realm or
  username is reported to the operator; the admin surface is operator-only, so enumeration
  defense does not apply there — amended at H4 implementation to match the setter's shipped
  design). The action is covered by all three test tiers.
- **R8** The network breach check runs at most once per account per configurable window; a
  sign-in inside the window skips the network call entirely. The offline common-passphrase check
  still runs on every sign-in. The existing kill-switch configuration keeps working.
- **R9** No page load or HTMX swap produces any CSP violation; the e2e suite asserts the
  violation list is empty (not merely filtered).
- **R10** Token and introspection endpoints conform to the flagged RFC points: Basic client
  credentials are percent-decoded (RFC 6749 §2.3.1); a request presenting credentials in both
  the header and the body is rejected; introspection without a token parameter is a 400
  (RFC 7662 §2.1); every grant type refuses to mint tokens for a deactivated user.
- **R11** The application database role has a neutral (non-`epass`) name; local development and
  CI both provision and use it; the legacy role no longer exists in provisioned databases.
- **R12** The published-app test fixture releases its process resources on every path including
  startup timeout, and contains no hardcoded OS/architecture identifier.

## Acceptance criteria
- Given an auto-provisioned account with only an email known to its user, when they complete the
  reset flow using that email, then they end with a working passphrase (R1).
- Given requests for reset codes against one account from many addresses, when the per-account
  limit is exceeded, then further issuance is refused while other accounts are unaffected (R2).
- Given a failure injected between the reset flow's final writes, when the request completes,
  then either all of its effects are visible or none are (R3).
- Given a database provisioned before and migrated by this work, when session rows are queried,
  then every row has an owner (R4).
- Given probes with unknown identifiers whose real-world domains end in uncommon TLDs, when the
  masked responses are compared with those for known accounts, then TLD distribution does not
  separate them (R5).
- Given a security key with no PIN/biometric, when it attempts the step-up for passphrase
  creation, then the ceremony fails (R6).
- Given a flagged account, when an operator submits the clear form for it, then the user signs
  in without being forced to reset; given an unknown username, the operator sees the same
  explicit not-found feedback the setter gives (R7, amended at H4).
- Given two sign-ins to one account within the window, when traffic to the breach service is
  observed, then at most one request was made; given the kill-switch off, none (R8).
- Given every e2e journey, when CSP reports are collected, then the list is empty (R9).
- Given a client secret containing percent-encoded characters, when it authenticates via Basic,
  then authentication succeeds; given credentials in both header and body, then the request is
  rejected; given introspection without a token, then the response is 400; given a deactivated
  user, when any grant type attempts to mint tokens for them, then it is refused (R10).
- Given a fresh clone and CI run, when the database is provisioned, then only the neutral role
  exists and all suites pass against it (R11).
- Given a startup timeout in the e2e fixture, when the run ends, then no orphan process or
  undisposed handle remains; given a Linux runner, then the fixture locates the published app
  (R12).

## Open questions
- (none — reset identifier = accept email, un-flag = stateless form, role rename = in scope;
  decided by the user 2026-07-31)
