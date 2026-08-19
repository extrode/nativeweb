# 008 — Sign-in library for epass.io relying parties
Status: draft
Created: 2026-08-19

## Problem

Every application that wants "sign in with epass.io" writes the whole client half itself. The one
that exists — justsmtp.com — carries **1,329 lines across nine files** of security-critical
plumbing before it has a single product feature: the login and callback endpoints, PKCE, the
signed session ticket, the single-use login-flow record, the open-redirect sanitiser, the
authentication handler, the wire DTOs, and the configuration binding.

None of that is justsmtp's. The next consumer will write it again, differently, and the
differences will be bugs — an open-redirect sanitiser is twenty lines that everyone writes with a
different hole in it. Worse, the parts most worth getting right are the parts a reimplementation
drops: justsmtp's client distinguishes *refused* from *unavailable* so a login page degrades
instead of throwing when the IdP is down, and its flow store makes a login link usable **at most
once** so a replayed link is dead. Both look like incidental detail and neither survives being
rewritten from memory.

Meanwhile NativeWeb claims to be a framework. Django ships `contrib.auth`, Rails has devise,
Laravel has Breeze. NativeWeb ships bot traps. A framework whose every consumer hand-rolls
authentication is a template with ambitions.

## Goals

- A new application signs in against epass.io by implementing **one interface** and supplying
  **one callback** — target under 10 lines of wiring, against 1,329 today.
- justsmtp.com runs on the library, with its nine local `Auth/` files deleted rather than
  deprecated. An extraction no consumer uses has not been tested, only compiled.
- The four behaviours that are easy to lose survive the move and are each pinned by a test:
  single-use login flows, refused-versus-unavailable degradation, boot-without-credentials, and
  upstream error text never reaching a page.
- The library does not drag the identity provider behind it. An app that only wants to *use*
  epass.io must not reference the library that *is* epass.io.
- No behavioural change to sign-in as experienced by a justsmtp user. This is an extraction, and
  a user-visible difference is a defect.

## Non-goals

- **A general OIDC client.** epass's wire format is not a standard and this does not pretend
  otherwise: the login-link start has no OIDC equivalent, and the DTO casing is inconsistent with
  RFC 6749 in ways that are epass's to fix. The library is named for the provider it speaks to.
  Supporting a second provider is a later decision informed by an actual second provider.
- **Changing the epass.io wire protocol**, or anything in `apps/web`. The IdP is the other side of
  this handshake and is out of scope entirely.
- **SAML, or any second federation protocol.**
- **Moving justsmtp's user model, page markup, or asset handling.** Those stay with the consumer
  by design, not by omission.
- **Publishing the library to a package feed.** Whether NativeWeb versions and publishes itself is
  an open question tracked separately in `work/todo.md`; 008 ships a project reference.

## User stories

- As an application author, I want to add epass.io sign-in by implementing one storage interface
  and one "who is this user locally" callback, so that I write no PKCE, no cookie signing and no
  callback endpoint.
- As an application author, I want my app to boot and serve its public pages when no epass
  credentials are configured, so that a fresh checkout runs without secrets.
- As an end user, I want a login link to stop working after I use it, so that a link recovered
  from my mail later cannot sign anyone in.
- As an end user, I want a clear local failure when the identity provider is unreachable, rather
  than an error page, so that the outage is legible and my other work is unaffected.
- As a reviewer, I want the security-critical code to have one implementation with one set of
  tests, so that a fix lands once instead of once per consumer.
- As a NativeWeb maintainer, I want the OAuth-client primitives to live outside the identity
  provider library, so that a consumer's dependency matches what it actually uses.

## Requirements

- **R1** A consumer obtains working sign-in by supplying configuration, one implementation of the
  login-flow storage contract, and one callback resolving a verified external subject to a local
  user. It writes no endpoint, no PKCE, no cookie handling and no callback logic.
- **R2** The library provides the login, callback and logout endpoints; the consumer chooses their
  paths.
- **R3** A login flow is consumable **at most once**. A second attempt on the same flow is
  rejected, and the rejection is distinguishable from an unknown flow and from an expired one.
- **R4** The storage contract carries no query language and no schema: the consumer owns the
  table, the library owns the contract.
- **R5** A sign-in attempt distinguishes three outcomes — success, refusal by the provider, and
  provider unavailable — and the consumer can act differently on each. Unavailability does not
  surface as an unhandled failure.
- **R6** An application with no epass credentials configured starts, serves its public pages, and
  reports sign-in as unconfigured rather than failing at startup or on the first request.
- **R7** Text originating from the identity provider is never rendered into a consumer's page. It
  may be logged.
- **R8** Every post-sign-in redirect target is sanitised against a consumer-supplied fallback, so
  that a crafted return path cannot send a signed-in user off-site.
- **R9** A session ticket is tamper-evident: a modified or truncated ticket is refused, and refusal
  is distinguishable from absence.
- **R10** The library does not depend on the identity-provider library. The OAuth-client
  primitives both need live where both can reach them without one depending on the other.
- **R11** The consumer keeps ownership of: local user provisioning, session cookie name, login page
  markup, and asset URLs. None acquires a library-supplied default.
- **R12** justsmtp.com uses the library, and its nine `Auth/` files no longer exist. Sign-in
  behaviour there is unchanged: same URLs, same cookie, same outcomes.
- **R13** Everything ships AOT-clean — no trim or AOT analyzer warnings, under repo-wide
  `TreatWarningsAsErrors`.
- **R14** The library is named for the provider it speaks to and claims no standards conformance in
  its name, its documentation or its public type names.
- **R15** Sign-in, sign-out and the callback are covered at all three tiers required before merge:
  unit, integration against a real database, and end-to-end.

## Acceptance criteria

- **R1** — Given a new application with none of this code, When it supplies configuration, a flow
  store and a sign-in callback, Then a user can complete sign-in, and the wiring is under 10 lines.
- **R3** — Given a login flow already consumed, When the same link is opened again, Then sign-in
  fails with an outcome distinct from "unknown flow" and no session is issued. Given a flow past
  its lifetime, Then the outcome is distinct from both.
- **R5** — Given the identity provider returns a refusal, Then the consumer can render its own
  "declined" path. Given the provider is unreachable or times out, Then the outcome reports
  unavailability and no unhandled exception escapes.
- **R6** — Given no credentials are configured, When the application starts, Then it serves public
  pages, and a sign-in attempt reports unconfigured without throwing at startup.
- **R7** — Given the provider returns a failure carrying descriptive text, When the consumer
  renders the resulting error page, Then no provider-supplied text appears in the response body.
- **R8** — Given a return path pointing off-site, or to a protocol-relative or scheme-bearing URL,
  When sign-in completes, Then the user lands on the consumer's fallback path.
- **R9** — Given a session ticket altered by one byte, When it is presented, Then it is refused,
  and the refusal is distinguishable from no ticket at all.
- **R10** — Given the library is built, When its dependencies are inspected, Then the
  identity-provider library is absent.
- **R12** — Given justsmtp.com on the library, When a user signs in, uses a stale link, signs out,
  and returns, Then every outcome matches today's behaviour, and the nine files are gone from the
  repository.

## Open questions

None blocking. Three decisions were taken before drafting and are recorded here so `/plan` does not
reopen them:

- **Scope includes migrating justsmtp.com**, not just producing the library. The seam is
  theoretical until an existing application goes through it.
- **The library is `nativeweb.signin.epass`.** Named for the capability and the provider. `rp` was
  rejected as jargon needing a glossary, `oidc` and `oauth` as claiming a standard the flow does
  not implement, and `idp` as naming the opposite role — the identity provider is `apps/web`.
- **The ring split happens here.** The OAuth-client primitives move out of the identity-provider
  library rather than being depended on or duplicated.

Deferred to `/plan` because they are HOW rather than WHAT: which primitives move and where they
land, the exact shape of the storage contract and its outcome type, endpoint path configuration,
and how the cross-repository change to justsmtp.com is sequenced against this one.
