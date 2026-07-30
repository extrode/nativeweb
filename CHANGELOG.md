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

### Changed
### Fixed
### Removed

---

## [0.1.0] — <date>

### Added
- (nothing yet)
