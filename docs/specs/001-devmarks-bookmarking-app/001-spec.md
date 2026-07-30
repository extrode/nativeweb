# 001 — DevMarks: a bookmarking app for software engineers

Status: draft
Created: 2026-07-30

## Problem

Software engineers accumulate hundreds of reference links — docs, blog posts, GitHub repos, Stack
Overflow answers — scattered across browser bookmarks on multiple machines and browsers. Browser
bookmarks don't carry notes, have weak tagging and search, and can't be selectively shared with
colleagues. Engineers end up re-googling things they already found, or pasting link dumps into
chat. DevMarks is a small hosted app that gives one engineer a fast, searchable, taggable,
selectively public bookmark collection. It is also the first production app on the Lean Native
Web architecture, proving the template end to end before the epass.io redesign.

## Goals

- An engineer can capture a bookmark in under ten seconds: paste a URL, get title, description
  and favicon filled in automatically, adjust, save.
- Any bookmark in a collection of thousands is findable in under three seconds via search or tag
  filter.
- A user can publish a chosen subset of bookmarks (individually or by tag) at a stable public
  URL, while everything else stays private by default.
- A user can move in and out with their data: import from any browser, export everything.
- The app is production-ready: safe against common web abuse, correct under concurrent use, and
  operable (health-checkable, observable) from day one.

## Non-goals

- No browser extension or bookmarklet (a later feature; the app must not depend on one).
- No teams, organizations, or shared editing — accounts are individual.
- No social features beyond public pages: no following, comments, likes, or discovery feed.
- No page archiving / read-later content capture — DevMarks stores links, not copies of pages.
- No AI features (summarization, auto-tagging).
- No native/mobile apps; the web app must simply work well on small screens.

## User stories

- As an engineer, I want to paste a URL and have the title, description and favicon fetched for
  me, so that saving a link takes seconds and the collection still looks rich.
- As an engineer, I want to tag bookmarks and filter by tag, so that "postgres" or "rust" links
  are one click away.
- As an engineer, I want full-text search over everything I've saved, so that I can find a link
  from a half-remembered phrase.
- As an engineer, I want my bookmarks private by default, so that saving something sensitive
  requires no thought.
- As an engineer, I want to mark a bookmark or a whole tag public, so that I can hand a colleague
  one URL for "my Postgres links" without exposing anything else.
- As an engineer, I want to import my existing browser bookmarks and export everything at any
  time, so that adopting or leaving DevMarks is painless.
- As a visitor without an account, I want a user's public page to be fast and readable, so that
  a shared collection is genuinely useful to me.

## Requirements

### Accounts and sessions
- R1. A visitor can register with a unique username, an email address, and a passphrase; the
  username becomes part of their public URL and is immutable after registration.
- R2. A registered user can sign in, sign out, and change their passphrase (providing the current
  one); a signed-in session persists across browser restarts until sign-out or expiry.
- R3. Repeated failed sign-in attempts against an account are throttled; the response to a wrong
  passphrase and an unknown username is indistinguishable.
- R4. All bookmark, tag, import/export and settings capabilities require a signed-in user and
  operate only on that user's own data.

### Bookmarks
- R5. A user can create a bookmark from a URL; a bookmark has a URL, title, optional description,
  optional free-text notes, an optional favicon, zero or more tags, a visibility (private or
  public), and created/updated timestamps.
- R6. On create (and on demand thereafter), the app fetches the URL and pre-fills title,
  description and favicon; the user can override any of them. A fetch that fails or is slow
  leaves the fields editable and empty and never blocks saving.
- R7. Metadata fetching cannot be used to reach the app's own network: requests to private,
  loopback, or link-local addresses are refused.
- R8. A user can edit any field of their bookmark, including its tags and visibility, and can
  delete a bookmark; deletion is confirmed before it happens.
- R9. Saving a URL the user has already bookmarked is detected and surfaced as an update to the
  existing bookmark, not a silent duplicate.

### Tags
- R10. Tags are per-user, case-insensitively unique, and created implicitly by typing them on a
  bookmark; a user can rename a tag (merging on collision) and delete a tag (which untags, not
  deletes, its bookmarks).
- R11. The user sees their tag list with per-tag bookmark counts and can filter their bookmarks
  by one tag, combined freely with search.
- R12. A tag has a visibility (private or public, default private); marking a tag public makes it
  a named public collection (see R16) without changing the visibility of individual bookmarks.

### Search and listing
- R13. A user's bookmark list shows newest-first, paginated, with title, domain, favicon,
  description, tags and visibility visible per row; page size is fixed and every page is
  addressable by URL.
- R14. Full-text search covers title, description, notes, URL and tag names of the user's own
  bookmarks; results respect any active tag filter and are ranked by relevance.

### Public pages
- R15. Every bookmark and tag is private by default; nothing a user saves is ever visible to
  anyone else unless the user explicitly marks that bookmark, or one of its tags, public.
- R16. Each user has a stable public page listing (a) their public bookmarks and (b) their public
  tags as named collections, each collection at its own stable URL, paginated, viewable without
  an account. Private bookmarks under a public tag do not appear.
- R17. A public page for a user with nothing public, or a private/unknown tag, is
  indistinguishable from a nonexistent page.

### Import and export
- R18. A user can import bookmarks from a Netscape bookmark HTML file (the export format of all
  major browsers) and from the app's own JSON format; folder structure maps to tags; imports are
  idempotent — re-importing the same file creates no duplicates; everything imported is private.
- R19. An import reports what happened: how many were created, updated, and skipped, with reasons.
- R20. A user can export all their bookmarks at any time as Netscape HTML and as JSON; a JSON
  export re-imports losslessly (tags, notes, visibility, timestamps preserved).

### Production readiness
- R21. All state-changing actions are protected against cross-site request forgery; user-supplied
  content (titles, notes, imported files) can never execute as script in anyone's browser.
- R22. Anonymous traffic to public pages and expensive authenticated actions (metadata fetch,
  import) are rate-limited so one client cannot degrade the service for others.
- R23. The app exposes a health check suitable for a load balancer and logs enough to diagnose a
  production incident (errors with correlation, auth events, import/export outcomes).
- R24. The app remains fully usable without JavaScript for core flows (sign-in, listing,
  searching, creating and editing bookmarks); enhancement is progressive.

## Acceptance criteria

- Given a signed-in user pastes a reachable public URL, when they save, then the bookmark appears
  at the top of their list with fetched title, description and favicon within a bounded time, and
  with visibility private. (R5, R6, R15)
- Given the pasted URL resolves to a loopback or private address, when the fetch is attempted,
  then it is refused, the user can still fill fields manually, and the save succeeds. (R6, R7)
- Given a user with 1,000 bookmarks searches a phrase from one bookmark's notes with a tag filter
  active, when results render, then that bookmark is present and only bookmarks carrying the
  filtered tag appear. (R11, R14)
- Given a user marks tag "postgres" public while its bookmarks stay private except two marked
  public, when a signed-out visitor opens that tag's collection URL, then exactly those two
  bookmarks are listed; the user's other tags and bookmarks are nowhere reachable. (R12, R15, R16)
- Given a visitor requests the public page of a user who has nothing public, when the page
  renders, then it is indistinguishable from the page for a username that doesn't exist. (R17)
- Given a user imports a 500-entry Netscape HTML export twice, when the second import completes,
  then the report shows 0 created and the bookmark count is unchanged. (R18, R19)
- Given a user exports JSON, deletes a bookmark, and re-imports the export, when the import
  completes, then the deleted bookmark is restored with its tags, notes, visibility and original
  timestamps. (R20)
- Given a client exceeds the rate limit on metadata fetches, when it retries, then it receives a
  throttled response and other users' fetches are unaffected. (R22)
- Given JavaScript is disabled, when a user signs in, searches, and creates a bookmark (without
  auto-fetch), then every flow completes via plain form submissions. (R24)
- Given ten failed sign-ins against an account, when the eleventh is attempted, then it is
  throttled, and the timing/content of failures does not reveal whether the username exists. (R3)

## Open questions

- None. Technology, schema and URL design belong to /plan (the binding stack is recorded in
  AGENTS.md and docs/native-web-architecture.md).
