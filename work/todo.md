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
- Analyzer risk pair (from V1 review, 2026-07-31): `TreatWarningsAsErrors` with unpinned
  `AnalysisLevel=latest-recommended` means an SDK bump can hard-break the build on new rules;
  consider pinning the level or adding a `WarningsNotAsErrors` escape hatch before the cutover.
- .editorconfig naming rules cover interfaces/types/members only — private fields, constants,
  locals and parameters are unenforced (V1 review note).

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- (nothing yet)
