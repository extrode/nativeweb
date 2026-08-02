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
- **Progressive enhancement layer + formatted HTML output** (spec 004, B1–B5) — the `data-nw-*`
  attribute vocabulary (`target`/`swap`/`trigger`/`confirm`) normalized to `hx-*` at runtime by
  `libs/nativeweb.assets/boot.js` (~100 lines, vendored per app with byte-parity tests, loaded
  `defer` before htmx; never overwrites authored `hx-*`, re-processes swapped-in descendants
  htmx's element snapshot misses); generator pretty-printer emitting indented HTML at build time
  (zero runtime cost, `pre`/`textarea`/`script`/`style` preserved, per-depth partial variants);
  both apps retrofitted — every IdP form now carries a hidden antiforgery input
  (`Layout.AntiforgeryField`) and every post-auth success site returns a real 302 to plain
  browsers (`Redirects.To`), making "works without JavaScript" true rather than aspirational;
  JS-off + normalization e2e coverage in both repos (incl. the settle-window and
  descendant-anchor swap paths); doc rewritten at `docs/progressive-enhancement.md`. Publishes:
  `nativeweb.web.exe` 35.2 MB (budget 42.5 MB), `manzil.exe` 29.1 MB, both zero-warning.
- **Architecture v2** (spec 003, V1–V10) — the template's second generation. Compiled-template
  render engine (`libs/nativeweb.render` + source generator: encoded-by-default HTML, typed
  params, partials/layouts, UTF-8 span literals) with an AngleSharp well-formedness gate;
  build-time i18n catalogs with dialect fallthrough (en + ms shipped, `lang`/`dir` negotiated
  per request); bot defense (4 honeypots + signed time canary on every form, fake-success
  discipline, e2e visibility assertions); security baseline (HSTS, `__Host-` cookie, 128 KB
  body cap, request timeouts, CSP reporting, append-only `audit_log`, `security.txt`, ASVS
  checklist, CI CVE gate); perf/ops (Brotli for HTML, off-by-default OTel, `/healthz` +
  `/readyz`, `ValidateOnStart`, publish knobs, CI size/startup budgets — baseline 36,918,272 B
  / 881 ms to `/healthz`); static-pages pipeline with nonce-free CSP and static 404/500;
  `skeleton/` tokenized product shell + `tools/new-product.ps1`; style enforcement in build.
- **manzil.my extraction** (spec 003 V10) — DevMarks moved out to
  `C:\home\code\extrode-legacy-org.com\manzil.my` as the first product stamped on the v2 architecture
  (`scripts/extract-manzil.ps1`: full manzil rename, fresh history, `$(NativeWebRoot)`
  dual-mode framework refs). Its suites run green there (217 unit / 81 integration / 12 e2e,
  0-warning AOT publish: 30,483,968 B / 977 ms to `/healthz`).

### Removed
- The DevMarks projects (`apps/devmarks`, `libs/nativeweb.devmarks.data`,
  `tests/nativeweb.devmarks.tests.*`, `db/devmarks`), now living in the manzil.my repo; CI,
  solution and docs rescoped to the IdP.
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
