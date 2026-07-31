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

## Later

Real, but needs a decision, a dependency, or more information first. Say which.

- CSRF (user, 2026-07-30): the framework antiforgery is kept for its BREACH-masked token, but the
  user dislikes how the `<meta name="request-verification-token">` + hidden-input plumbing looks.
  Candidate: a Rails-style per-session token behind a small helper so the markup reads cleaner —
  needs a decision on trading away per-request masking.
- DevMarks: formatted-HTML-source retrofit for the slice-1 pages (user asked for properly
  formatted markup; deferred from the scaffold). Explicitly left out of the T9 hardening pass on
  2026-07-30: cosmetic, not an acceptance criterion — new markup since the scaffold is indented.

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- (nothing yet)
