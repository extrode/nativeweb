# 006 — Cacheable public pages

Status: done
Created: 2026-08-02

## Problem

Manzil's crawlable anonymous pages — the home page and the `/u/{username}` public profiles and
collections — are rebuilt from the database on every request, because rendering them mints
per-request state: an antiforgery token pair (cookie + meta tag + hidden inputs) and a CSP
script nonce. That state exists for the sign-in chrome and the inline scripts, not for anything
the anonymous reader does, yet it forces every response to be unique and uncacheable. Under
load the public list page costs ~7–10ms and carries the whole tail of the app's latency
distribution; a cacheable page would serve in ~1ms flat and shield the database from anonymous
crawl traffic entirely. The user approved this direction 2026-08-02 (perf research follow-up,
see `docs/plans/2026-08-02-002-perf-tail-work.md`), with the explicit constraint that security
must not be weakened.

## Goals

- Anonymous responses for `/` and `/u/*` are servable from a cache (server-side output cache,
  shared proxy, or both) — repeat anonymous requests do not touch PostgreSQL.
- Public-page latency under load approaches the app's static/SSR floor (~1–2ms median) and the
  DB-backed tail (p95 spikes on `/u/*`) disappears from anonymous traffic.
- A signed-in visitor still sees their personalized chrome (username, nav, flash) everywhere,
  with no cache poisoning between the anonymous and signed-in variants.
- Content freshness is bounded and stated: a bookmark save/visibility change appears on the
  owner's public page within a defined window (or immediately, if invalidation is chosen in
  the plan).
- The security posture is unchanged or stronger: CSP still blocks unauthorized inline script,
  antiforgery still guards every state-changing route, and the enumeration defense on `/u/*`
  404s (uniform status, body and timing) survives caching.

## Non-goals

- Caching any authenticated or personalized response.
- Caching the sign-in, register, or any form page whose POST is antiforgery-protected — unless
  the plan shows the token acquisition can move without weakening the protection.
- CDN/edge deployment design (the pages should become *cacheable*; where a cache is deployed
  is a cutover concern).
- The IdP (apps/web): its anonymous surface is one landing page; adopt later by pattern-copy
  if the manzil design proves out.
- Rewriting the flash mechanism (flash renders only after redirects from state-changing
  actions, which are signed-in flows).

## User stories

- As an anonymous reader following a shared `/u/{username}` link, I get the page fast even
  when the site is busy, because no database work runs for me.
- As a bookmark owner, I mark a bookmark public and can tell (within the stated freshness
  window) that my public page shows it.
- As a signed-in user browsing my own public page, I still see my signed-in chrome and never
  someone else's, and never a stale anonymous shell presented as my session.
- As the operator, I can put a caching proxy in front of the app without leaking one user's
  session-decorated page to another.

## Requirements

- R1: An anonymous request to `/` or `/u/*` produces a response that carries no per-request
  secret (no antiforgery token, no per-response CSP nonce) and no session-derived content.
- R2: Responses on those routes declare their cacheability and variance explicitly (headers
  chosen in the plan), such that a standards-compliant shared cache stores the anonymous
  variant and never serves it to a request bearing a session cookie (nor vice versa).
- R3: Inline scripts on cacheable pages remain CSP-authorized without a per-response nonce,
  with no broadening of the effective script-src policy (no `unsafe-inline` without a
  compensating hash/allowlist mechanism of equivalent strength).
- R4: Every state-changing route keeps antiforgery protection with per-session-or-stronger
  tokens; any page that hosts a protected form either stays uncacheable or acquires its token
  through a mechanism that does not weaken the protection.
- R5: The `/u/*` enumeration defense holds: all four "nothing here" flavors stay
  indistinguishable by status, body and timing — including cache behavior (a cached hit for a
  once-valid page must not make a missing page distinguishable beyond what an uncached app
  already reveals).
- R6: Public-page freshness is bounded: the maximum staleness after a bookmark, tag or
  visibility change is stated, tested, and no more than a small number of minutes — or zero if
  invalidation is chosen.
- R7: The signed-in experience on `/` and `/u/*` is byte-equivalent to today's (chrome,
  flash, active nav), whatever caching does for anonymous traffic.
- R8: Rate limiting on the public group still applies to requests that reach the app; a cache
  in front reduces load but its absence must not change correctness.
- R9: All three test layers cover: anonymous cacheability (headers + no secrets in body),
  variant separation (session cookie → personalized, never cached-anonymous), CSP validity of
  the served pages both ways, and the freshness bound.

## Acceptance criteria

- Given an anonymous GET of `/u/{user}` twice in the freshness window, when a shared cache
  sits in front, then the second response is a cache hit, byte-identical, contains no
  antiforgery meta/input and no nonce attribute, and the app served at most one DB-backed
  render.
- Given a signed-in GET of the same URL, then the response is personalized, marked
  uncacheable-for-shared-caches, and a subsequent anonymous GET does not receive it.
- Given a bookmark flipped to public, when the freshness window elapses, then the anonymous
  `/u/{owner}` page shows it (or immediately, under invalidation).
- Given the browser console on any cached page, then no CSP violation is reported and every
  inline script executes.
- Given the existing public-pages integration suite (enumeration, visibility, clamping), then
  it passes unchanged in meaning.

## Open questions

(For `/plan` — implementation decisions deliberately not made here.)

- CSP mechanism for cacheable pages: hash-based `script-src` for the two (build-time-known)
  inline scripts vs externalizing them to fingerprinted static files vs strict-dynamic. The
  inline scripts are byte-stable at build time, which makes hashes natural — but the hash set
  must stay in lockstep with the templates via the build, never hand-maintained.
- Sign-in chrome on cacheable pages: the anonymous variant's "Sign in" link needs no token;
  confirm nothing else in the anonymous chrome depends on per-request state (bot canary?).
- Where the anonymous/signed-in fork happens: render two variants server-side keyed by session
  presence (`Vary` on the cookie? split URLs? out-of-band hydration?) — chosen against real
  cache semantics, not hoped-for ones.
- Output-cache (in-process) vs proxy-cache (headers-only) vs both; and whether `/u/*` 404s are
  cached (R5 interacts: cached 404s must have the same shape as cached 200s' misses).
- Freshness: TTL-only vs event invalidation (save/visibility/tag endpoints already know the
  owner — invalidation is one cache-evict per write) — decide by operational simplicity.
- Does `/sign-in` itself stay uncacheable (simplest, likely yes), and does the register page
  follow.

## Outcome (2026-08-03)

Shipped on `feat/cacheable-public-pages` (manzil.my) and `chore/spec-006-skeleton-parity`
(NativeWeb). Every acceptance criterion above is covered by a test that fails when the
behaviour is removed.

Measured with k6, 10 VUs against the published binary, `/u/{user}` with 40 public rows,
same URL and same body on both paths — the only difference is whether the output cache
answers:

| path | median | p90 | p95 | max |
| --- | --- | --- | --- | --- |
| anonymous, cache hit | <0.1ms | 545µs | 580µs | 16.9ms |
| flash cookie, DB-backed render | 1.17ms | 1.80ms | 2.11ms | 192ms |

The median drops by roughly an order of magnitude and, more to the point, the DB tail
(192ms worst case) leaves anonymous traffic entirely. These are local-machine numbers on a
warm 40-row dataset, so the DB-backed column is far better than the 7–10ms the Problem
section quotes for production; the ratio is the finding, not the absolute.

Two defects were found by this work rather than by review, both fixed in the branch:

- **Cache replay rebuilt the CSP from scratch.** The public-page mark lived in
  `HttpContext.Items`, which is empty on a hit because the handler never runs — so every
  replayed response carried a nonce CSP over a nonce-free body, blocking each inline
  script. The mark is now endpoint metadata, making the headers a function of
  (route, cookies): the same inputs the cache key and the bypass use.
- **A 429 was labelled publicly cacheable.** A rate-limited request reaches the same
  endpoint metadata, so it was served with `Cache-Control: public, max-age=60` — enough
  for a shared proxy to hand one client's rejection to every anonymous visitor for a
  minute. The header rule is now gated on a 200, matching what the cache policy already
  refused to store.

Review then found three more, all fixed in the branch:

- **The CSP hash depended on the checkout's line endings.** Hashes were taken over the
  authored bytes, but a browser newline-normalizes the HTML input stream before tokenizing,
  so it hashes the LF form whatever the file holds. The skeleton's CRLF `layout/page.html`
  therefore emitted constants matching nothing, and both inline scripts on its cacheable
  home page would have been blocked. `HashSource` now normalizes before hashing; the served
  bytes are left alone. manzil was unaffected — its hashed bodies were already LF.
- **`Vary` was assigned, erasing `Vary: Accept-Encoding`.** `UseResponseCompression` sets it
  from a separate `OnStarting` callback, and those fire in reverse registration order, so
  the assignment landed last — after which a shared cache could hand a brotli body to a
  client that never asked for one. It appends now.
- **Eviction was status-gated and took the request's cancellation token.** A handler that
  throws after committing produces a 500 over a change that really landed, and a client
  disconnecting as the response flushes could cancel the invalidation of its own write.
  Both gates are gone; the logic moved to `EvictAsync` so a unit test can assert properties
  no flow test can see.

An out-of-range `?page` is now rendered but not stored, so a clamped page cannot be used to
flood the cache with keys. The handler decides, because it is the only party that knows the
real page count.

Out of scope, noted in `work/todo.md` rather than fixed here: username lookups are
case-sensitive in practice (a CITEXT column compared against an explicitly-typed `text`
parameter), and the IdP's `AppProcessFixture` shares the undrained-stdout bug fixed in
manzil's e2e fixture under T9.
