# DevMarks — technical plan

> Plan for spec `docs/specs/001-devmarks-bookmarking-app/001-spec.md`. On approval this file is
> promoted to `docs/specs/001-devmarks-bookmarking-app/001-plan.md` and committed in the branch's
> first commit.

## Context

NativeWeb's end goal is unchanged: epass.io redesigned in v2's green UI as a single NativeAOT
app. Scope change (2026-07-30): first prove the Lean Native Web template end to end with a
smaller production-ready app — **DevMarks**, a bookmarking app for software engineers (spec 001):
accounts, bookmark CRUD with auto-fetched metadata, tags, full-text search, private-by-default
with opt-in public pages, Netscape/JSON import/export, rate limiting, no-JS-capable core flows.

**Hard constraints (user, 2026-07-30):**
- **No existing work is thrown away.** The IdP app (`apps/web`), all `libs/`, `tests/`, `db/` and
  docs are untouched. DevMarks is purely additive. (The `epass.*` dirs under `libs//tests/` are
  stale `bin//obj` leftovers from the N4 rename — left alone, listed for the cleanup script.)
- **Real-world project structure.** Full production layout: own projects under the repo's
  `apps//libs//tests/` convention, three-tier tests, migrations, config, health endpoints,
  README, CI coverage.

## Design decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Auth model | Standalone accounts in DevMarks (username/email/passphrase), reusing `libs/nativeweb.security` (Argon2id) + `libs/nativeweb.auth` (LockoutPolicy, breach/common-passphrase checks) | Spec R1–R4; keeps the app independently deployable; still exercises the shared libs. OAuth-against-the-IdP is a later option, not now |
| Database | Own database `devmarks_dev` / role `devmarks_app`; DB assets under `db/devmarks/{migrations,queries,schema}` | Root `db/` is the IdP's and feature-frozen; a separate DB avoids entangling schemas and gives the neutral role name the IdP never got. `nativeweb.data`'s globs (`db/queries/**`) don't reach `db/devmarks/**` — no collision |
| Data access | New lib `libs/nativeweb.devmarks.data`, same JauntyQ Analyzer+Runtime wiring as `libs/nativeweb.data`, globbing `db/devmarks/queries/**/*.sql` + `db/devmarks/schema/*.schema.json` | The existing data project is hardwired to the IdP's `db/`; copying its ~30-line csproj is the designed-for path (its README anticipates more consumers) |
| Search | Postgres FTS: stored generated `tsvector` column on `bookmarks` (title=A, description=B, notes=C, url=D) + GIN index; `websearch_to_tsquery`; tag names matched via an OR'd EXISTS against the user's tags | R14 incl. relevance ranking (`ts_rank`); no extensions beyond what `db/` already uses |
| Favicons | Fetched server-side, stored as capped bytes (≤64 KB, image content-types only) in the bookmark row, served from `/bookmarks/{id}/favicon` with long cache | No hotlinking third-party requests from visitors' browsers; survives origin outages |
| Sessions | Copy the IdP's opaque-cookie pattern (`apps/web/Auth/SessionAuth*.cs`) against a DevMarks `sessions` table, 14-day sliding | Proven AOT-safe pattern; the IdP's handler is coupled to `bff_store` so it's a copy, not a reference |
| Rendering | Copy the Render pattern: `Layout.cs` (CSP nonce, antiforgery meta, HTMX glue), `AssetUrls.cs`, page-per-file static classes with raw-string interpolation + `HtmlEncoder`; vendored htmx/alpine copied into DevMarks' `wwwroot/vendor/` | None of it lives in a lib; copying is the explicit repo convention for app #2 |
| Naming | App `apps/devmarks` → `nativeweb.devmarks.csproj`, namespace `NativeWeb.DevMarks` | Matches the repo's `nativeweb.*` project family |

## Project layout (all new, nothing moved)

```
apps/devmarks/                        nativeweb.devmarks.csproj  (PublishAot, CreateSlimBuilder)
  Program.cs                          composition root, IdP Program.cs pattern
  Auth/        Sessions, sign-in/register/settings endpoints
  Bookmarks/   list/search/create/edit/delete/favicon endpoints + metadata fetcher
  Tags/        tag list/rename/delete/visibility endpoints
  Public/      /u/{username} pages
  Transfer/    import/export endpoints + Netscape HTML and JSON codecs
  Middleware/  SecurityHeaders, ErrorPage, RequestLogging (copied pattern)
  Render/      Layout, AssetUrls, Banner, one file per page/fragment
  wwwroot/     css tokens + vendor/htmx,alpinejs (copied), favicon etc.
  README.md
libs/nativeweb.devmarks.data/         JauntyQ generator host for db/devmarks
db/devmarks/
  migrations/0001_*.sql …             numbered, schema_migrations-tracked
  queries/<Entity>/*.sql              one file per operation + MAPPING.md
  schema/devmarks.schema.json         JauntyQ schema snapshot
  migrate.sh                          copy of db/migrate.sh pointed at devmarks_dev/devmarks_app
tests/nativeweb.devmarks.tests.unit          xUnit (render, codecs, SSRF guard, policies)
tests/nativeweb.devmarks.tests.integration   WebApplicationFactory + real PG (devmarks_dev/_test)
tests/nativeweb.devmarks.tests.e2e           Playwright vs published AOT exe (own port, e.g. 5345)
```

## Data model (db/devmarks/migrations)

- `users` — id, username (citext unique, immutable), email (citext unique), passphrase_hash,
  created_at; `sessions` — opaque token hash → user, created/expires, sliding.
- `login_attempts` — feeds the reused `LockoutPolicy` (5 fails/15 min).
- `bookmarks` — id, user_id, url, url_canonical (per-user unique for R9 dedup), title,
  description, notes, favicon (bytea, nullable) + favicon_content_type, is_public,
  created_at/updated_at, `search tsvector` generated column, GIN index.
- `tags` — id, user_id, name (citext, per-user unique), is_public; `bookmark_tags` — join.
- Grants mirror `db/migrations` style: app role `devmarks_app`, superuser only for the citext
  extension migration.

## URL map

Authed (all under antiforgery + `auth`/`app` rate limiters, every page works without JS):
`/` `/register` `/sign-in` `/sign-out` `/settings` (passphrase change) ·
`/bookmarks?q=&tag=&page=` (list/search/filter; HTMX partial refresh, full page without JS) ·
`/bookmarks/new` (POST fetches metadata unless `manual=1`) · `/bookmarks/{id}/edit` ·
`/bookmarks/{id}/delete` (confirm page → POST) · `/bookmarks/{id}/favicon` ·
`/tags` (+ rename/delete/visibility POSTs) · `/import` (multipart, report page) ·
`/export.json` `/export.html`.
Public (anonymous, `public` rate limiter): `/u/{username}?page=` ·
`/u/{username}/t/{tag}?page=` — 404-identical for unknown/empty/private (R17).
Ops: `/healthz` `/readyz` (same contract as the IdP's).

## Metadata fetch (SSRF-guarded, R6/R7)

`MetadataFetcher` in `apps/devmarks/Bookmarks/`: http/https only; resolve DNS then reject
loopback/private/link-local/CGNAT ranges (re-check on each of ≤3 redirects); 5 s timeout; read
≤512 KB HTML; extract `<title>`, meta description, favicon link (fallback `/favicon.ico`);
favicon download same guards, ≤64 KB, image/* only. Failure → empty editable fields, save never
blocked. Unit-tested against a local test server; the guard logic is pure and tested directly.

## Reuse map

| From | Used for |
| --- | --- |
| `libs/nativeweb.security` (Passphrases/Argon2id, OpaqueTokens, Sha256Hex) | passphrase hashing, session tokens |
| `libs/nativeweb.auth` (LockoutPolicy, breach/common-passphrase checks) | R3 throttling, registration hygiene |
| `libs/nativeweb.devmarks.data` (new, pattern from `libs/nativeweb.data`) | all SQL |
| `Directory.Build.props` / `Directory.Packages.props` | inherited unchanged (AOT, warnings-as-errors, central pins) |
| Copied patterns from `apps/web`: Program.cs composition, Middleware/, Render/, session handler, CSS tokens, vendored htmx/alpine | app shell |
| Test patterns: `NativeWebFactory` env-var static-ctor overrides, `AppProcessFixture` (publish + run exe, `/healthz` poll, kill tree — includes last session's two known fixes: dispose on timeout, no hardcoded win-x64 in the *new* fixture), `TestDatabase` side-channel | all three tiers |

## CI (`.github/workflows/ci.yml`, additive edits only)

`test` job: create `devmarks_test` DB + `devmarks_app` role in the existing postgres:18 service,
psql-loop `db/devmarks/migrations`, run the three new test projects (same `-p:JauntyQUsePackages=true`
caveat — the JauntyQ feed gap remains a known, separate issue). `aot-publish` job: add a publish
line for `apps/devmarks/nativeweb.devmarks.csproj` (the zero-AOT-warnings gate).

## Implementation slices (each: branch off dev → code+tests → review → merge `--no-ff`)

1. **Scaffold** — projects, solution entries, db/devmarks + 0001 migrations, data lib, Program.cs
   with health endpoints, middleware, layout, CSS, vendored assets, empty home page; all three
   test projects boot (factory + AppProcessFixture green); CI additions.
2. **Accounts** — register/sign-in/sign-out/settings, sessions, lockout, breach check (R1–R4).
3. **Bookmarks core** — CRUD, dedup-on-save, list + pagination (R5, R8, R9, R13).
4. **Metadata fetch** — SSRF guard, title/description/favicon, favicon endpoint (R6, R7).
5. **Tags** — implicit create, rename/merge, delete, counts, filter (R10, R11).
6. **Search** — FTS + ranking combined with tag filter (R14).
7. **Public pages** — visibility flags, /u/ pages, 404-indistinguishability (R12, R15–R17).
8. **Import/export** — Netscape HTML + JSON codecs, idempotent import, report, lossless
   round-trip (R18–R20).
9. **Production hardening** — rate limiters on public/fetch/import, request logging, no-JS pass,
   small-screen pass, README, spec acceptance-criteria sweep (R21–R24).

Each slice ships its unit + integration coverage; e2e flows land with slices 2, 3, 7, 8 and get a
full-suite pass in slice 9. `work/tasklist.md` + `docs/specs/001-*/001-tasks.md` track this via
/tasks after plan approval.

**Delivery strategy (user, 2026-07-30: time is of the essence; visual ASAP; ≤4–5 workers).**
- **First tangible milestone**: slice 1 is extended to a *visible* app — styled layout (green v2
  token system), home page, and a bookmarks list rendering seeded demo data — published and
  running locally so the user can see and judge the look immediately. Screenshot delivered.
- **Parallelism**: after slice 1 lands on dev, fan out independent slices to codegen workers in
  separate worktrees (≤4 at a time), e.g. accounts ∥ metadata-fetch ∥ import-export codecs ∥
  tags — each self-contained with its tests. Migrations are numbered centrally by the parent to
  avoid collisions; the parent sequences merges. Search and public pages follow (they depend on
  bookmarks+tags), then the hardening pass.

## Verification

- Per slice: `dotnet test` unit + integration (needs local `devmarks_dev` migrated via
  `db/devmarks/migrate.sh`), then Playwright e2e where the slice adds flows.
- End state: full three-tier suite green; `dotnet publish apps/devmarks -c Release` with zero
  AOT/trim warnings; manual acceptance sweep of every spec acceptance criterion, JS disabled for
  the R24 pass; screenshot check of list/public pages.
- Existing IdP suite still green (proves nothing was disturbed).

## Out of scope / notes

- No changes to `apps/web`, `db/` (root), or existing libs. Stale `epass.*` bin/obj leftovers →
  cleanup script candidate, not touched.
- Backlog items from the previous session (CSP indicator, HIBP throttle, etc.) stay in the IdP
  backlog; the DevMarks copies of those patterns incorporate the known fixes from day one.
