# Todo — NativeWeb

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Now-ish

Small, clear, and could be picked up without a design decision.

- DevMarks: `apps/devmarks/wwwroot/vendor/webauthn/` was copied from the IdP unintentionally and
  is unused — tracked files, so it needs a small chore branch with `git rm`, not the cleanup
  script (`scripts/cleanup/devmarks-app.ps1` covers the untracked leftovers and notes this).
- DevMarks: unit tests reach `SecurityHeadersMiddleware` internals via reflection; add an
  `InternalsVisibleTo` for `nativeweb.devmarks.tests.unit` (as apps/web does) and call directly.

- IdP: passphrase reset/verify success responds with only an `HX-Redirect` header
  (`Results.Empty`), which a no-JS browser ignores — the non-htmx `method=post action=...`
  fallback completes the reset but shows a blank page. Return a plain 303 for non-htmx requests
  (H5 review nit, 2026-07-31).

- DevMarks: local `devmarks_dev` has drift — `db/devmarks/migrate.sh` fails on 0006
  ("search_match already exists") because the function was applied without being recorded in
  schema_migrations. Needs a one-line `INSERT INTO schema_migrations (version) VALUES ('0006')`
  or an idempotent CREATE OR REPLACE in 0006 (noticed 2026-07-31 during the role-rename check).

## Later

Real, but needs a decision, a dependency, or more information first. Say which.

- CSRF (user, 2026-07-30): the framework antiforgery is kept for its BREACH-masked token, but the
  user dislikes how the `<meta name="request-verification-token">` + hidden-input plumbing looks.
  Candidate: a Rails-style per-session token behind a small helper so the markup reads cleaner —
  needs a decision on trading away per-request masking.
- DevMarks: formatted-HTML-source retrofit for the slice-1 pages (user asked for properly
  formatted markup; deferred from the scaffold). Explicitly left out of the T9 hardening pass on
  2026-07-30: cosmetic, not an acceptance criterion — new markup since the scaffold is indented.

- Render engine deferred review findings (V2 review, 2026-07-31; the fixable ones were fixed
  in-branch, these need design work):
  - URL-context safety: `href="{{Url}}"` with a `javascript:` value passes both encoder paths
    untouched (same gap as Razor). Full fix is HTML-context-aware generation; interim is the
    documented convention that URL values from user data get scheme-validated in C#.
  - Unquoted-attribute interpolation (`<a href={{Url}}>`) is injectable via space/`=`; the
    generator could detect-and-reject interpolation into unquoted attributes.
  - NWT diagnostics all report at `Location.None`; parser would need to carry line/col offsets
    to point at the token.
  - `@param` type text goes into the generated signature unvalidated (typo = raw C# error, not
    NWT003).
  - No escape hatch for a literal `{{` in template text (would break Vue-style inline syntax if
    ever needed; Alpine/htmx don't use it).
  - Diagnostics tests discard the output compilation, so a template emitting invalid C# only
    fails via the test project's own build, not a targeted assert; no incremental-caching
    step-reuse test either.
- i18n deferred review findings (V3 review, 2026-07-31; the mechanical ones were fixed
  in-branch, these need design work or a future trigger):
  - Locale negotiation runs before `UseAuthentication`, so the (deliberately unwired)
    `UserPreference` hook would always see an unauthenticated context — move the middleware
    after auth when the hook lands with IdP page localization (comment in Program.cs marks it).
  - One NWT014/NWT015 suppresses the whole generated `Locales` class, so every `Locales.*`
    reference cascades CS errors that bury the real diagnostic; a best-effort/empty-catalog
    emission would keep it legible. Same shape for missing en.json → every `{{t:}}` fires NWT013.
  - Locale ids are two-subtag only: `zh-hans-cn` would get wrong `Lang` casing and inherit from
    `zh`, never `zh-hans` (documented at `LocaleCatalog.ToHtmlLang`); extend if a script-tagged
    locale ever ships.
  - Sanitize boundary is inconsistent in mail: renderer args are control-char-stripped but
    catalog-sourced subject/text are not (MimeKit encodes headers, so not injectable), and
    `Sanitize(linkUrl)` mangles a control-char URL into a different URL rather than rejecting it.
  - Templates and locales share one `RegisterSourceOutput`, so editing one .json re-emits every
    template and vice versa (pre-existing shape; incremental win currently nil).
  - `users.locale` is unbounded free text by design (unknown degrades to English); whatever
    preference-setting endpoint lands later must validate against `Locales.All`.
- Analyzer risk pair (from V1 review, 2026-07-31): `TreatWarningsAsErrors` with unpinned
  `AnalysisLevel=latest-recommended` means an SDK bump can hard-break the build on new rules;
  consider pinning the level or adding a `WarningsNotAsErrors` escape hatch before the cutover.
- .editorconfig naming rules cover interfaces/types/members only — private fields, constants,
  locals and parameters are unenforced (V1 review note).

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- (nothing yet)

## Deferred from V4 review (2026-07-31)
- Per-field errors: endpoints surface validation failures as page-level banners (parity with the
  old markup); form-field.html's Error/aria-invalid path is ready but every caller passes
  Error="". Thread real per-field errors when the UX is revisited.
- Fragment-assembled sentences in bookmarks/index.html empty states and bookmark-row's
  "Saved {date}" fix English word order across locales; replace with parameterized keys +
  precomputed C# strings.
- Results.Redirect("/sign-in") on auth-expired paths ignores HX-Request across all endpoint
  files (pre-existing); htmx swaps a full document into a fragment slot. Route through
  SignInEndpoints.RedirectResult everywhere. Same for the already-signed-in redirects on
  GET /sign-in and /register. Also: RedirectResult's comment says 303 but emits 302.
- ValidateAsync (4 files) checks antiforgery before HasFormContentType, so a non-form POST
  gets 400 not the intended 415 (pre-existing).
- Anonymous pages (home, /u/*) mint an antiforgery cookie via Chrome.For/GetAndStoreTokens,
  making the crawlable pages uncacheable (pre-existing convention) — revisit in V7 perf work.
- Home route sits outside the "public" rate-limit group (pre-existing) — revisit in V5/V6.
- "1 tags" on /tags (tags.count has no singular variant) and count_saved shown for
  tag-filtered lists (both parity with old markup) — wording pass later.
- Nav shows Bookmarks/Tags/Import to signed-out visitors and never marks Settings active
  (both parity with the old layout) — UX pass later.
- GenericFailureMessage const duplicates error.signin_generic for the integration tests;
  point the tests at Locales.Get("en") and drop the const.
- Single-page lists render a pagination landmark with only position text (parity).
- tools/convert-*.py are the R42 conversion scripts, kept as reference for the IdP template
  migration spec; delete with that spec's close-out.
- (V6 review, fable) apps/web stores the session cookie value as the bff_store row's
  plaintext UUID PK, so any DB read yields live bearer tokens; devmarks stores only the
  SHA-256 of its token. Hash the IdP's at rest the same way (pre-existing, pre-cutover).
- (V6 review, fable) devmarks has no DataProtection key persistence, so every restart
  invalidates outstanding antiforgery tokens; mirror web's PersistKeysToFileSystem block.
- (V6 review, fable) devmarks loads user-secrets unconditionally rather than
  Development-only (web gates it); align.
