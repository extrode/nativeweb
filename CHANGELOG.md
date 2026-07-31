# Changelog — NativeWeb

What shipped, newest first. One entry per release, not per commit: the git log already records
every commit, and a changelog that repeats it is read by nobody.

An entry is added when `dev` merges to `main` and gets its semver tag. Work that is merged into
`dev` but not yet released belongs in `status/previous.md`, not here.

Format is [Keep a Changelog](https://keepachangelog.com/), versions are semver. Use only the
sections that apply; drop the empty ones rather than leaving them blank.

## [Unreleased]

Merged into `dev`, not yet tagged.

### Added
- **DevMarks** (`apps/devmarks`, spec 001) — a complete bookmarking app for software engineers
  and the first full application on the Lean Native Web template: accounts (Argon2id
  passphrases, lockout, breach checking), bookmark CRUD with SSRF-guarded metadata/favicon
  fetch, tags with rename-merge and per-tag visibility, ranked Postgres full-text search
  (title/description/notes/URL/tag names), private-by-default public pages (`/u/{username}`,
  `/u/{username}/t/{tag}`, indistinguishable 404s), Netscape HTML + JSON import/export with
  idempotent re-import and lossless JSON round-trip, fixed-window rate limiting, structured
  request logging, health endpoints. Every core flow works without JavaScript. Own database
  (`db/devmarks`), JauntyQ data layer (`libs/nativeweb.devmarks.data`), three test tiers
  (196 unit / 56 integration / 7 e2e), zero-warning NativeAOT publish, CI coverage.

- **IdP passphrase-reset by email** (spec 002) — the reset flow accepts an email address as well
  as a username, so auto-provisioned passwordless accounts that have never seen their generated
  username can complete a forced reset unaided. Copy updated ("Username or email").
- **Operator un-flag** (spec 002) — a stateless clear form on `/admin/accounts` lifts a forced
  passphrase reset via the same (realm, username) lookup that sets it, with the setter's
  explicit feedback; covered by all three test tiers.

### Changed
- **IdP hardening close-out** (spec 002, `docs/specs/002-idp-hardening/`) — the deferred
  security/conformance backlog closed across seven reviewed branches:
  reset-code issuance capped per identifier (30 s floor + 5/24 h rolling window, one atomic
  conditional upsert); reset success writes are transactional; synthetic identifier masks use
  the full IANA TLD registry and contact-shaped identifiers mask as themselves, closing the
  TLD/shape existence oracles; pre-stamping `bff_store` session rows backfilled with their
  owner (migration 0049); the per-sign-in HIBP network check throttled to once per account per
  configurable window (default 24 h, migration 0051 + `BreachResult.NetworkChecked`), with the
  offline common-passphrase list still on every sign-in; token/introspection endpoints brought
  to RFC 6749/7662 conformance on the flagged points (percent-decoded Basic credentials,
  dual-credential rejection, tokenless introspection → 400, active-user re-check on every
  grant); htmx's un-nonced indicator stylesheet disabled so no page produces a CSP violation
  (e2e asserts an empty violation list); the application database role renamed to the neutral
  `nativeweb_app` in migrate.sh, CI and local dev; the e2e app fixture disposes its process on
  the startup-timeout path and derives the RID instead of hardcoding `win-x64`.
  Final state: 319 unit / 206 integration / 44 e2e green, zero-warning NativeAOT publish.

### Fixed
### Removed

---

## [0.1.0] — <date>

### Added
- (nothing yet)
