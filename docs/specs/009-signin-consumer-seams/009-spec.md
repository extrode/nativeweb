# 009 — Consumer seams for the sign-in library
Status: draft
Created: 2026-08-24

## Problem

Spec 008 extracted sign-in into `nativeweb.signin.epass` and migrated its first consumer,
justsmtp.com, onto it. That migration worked — 1,329 lines across nine files became 543 across
six — and in working it found three places where the library takes something over and gives
nothing back. All three are recorded in justsmtp's `work/todo.md`, each next to the workaround it
forced.

The three share a shape. The library correctly claims a responsibility, and correctly says in its
own comments that some neighbouring decision belongs to the consumer — but leaves the consumer no
way to make it. A seam named in a comment and absent from the API is not a seam.

- **Ending a session mid-request.** `SessionAuth.Clear` is `public` on an `internal` class and
  `SessionAuthenticationHandler` implements no `IAuthenticationSignOutHandler`, so
  `HttpContext.SignOutAsync` is unavailable. The only sanctioned sign-out is a form post to the
  logout path, which a request filter cannot perform. A consumer that discovers mid-request that
  an account is disabled therefore hand-copies the cookie attributes — and a delete whose
  attributes drift from the library's is a no-op the browser never reports, leaving a user signed
  in while every page says they are not. justsmtp does exactly this today in
  `apps/web/Dashboard/DashboardExtensions.cs`.

- **Minting a session in a test.** `SessionCookie` is `internal`, correctly: a public `Mint` is an
  authentication bypass any consumer can call with any subject. But a consumer's integration
  tests still need a signed-in browser, so justsmtp's `SessionTests.cs` reimplements the v2 ticket
  format by hand — which its own csproj comment forbids, on the grounds that a test signing its
  own cookies its own way asserts only that the test can sign cookies. When the format next
  changes, that test keeps passing against a format nothing ships.

- **Deciding whether an address is plausible.** The library owns the login POST and bounds only
  size, on the stated grounds that plausibility is the consumer's form and the consumer's message.
  The consumer has no seam to say so in: justsmtp's `IsPlausibleEmail` was deleted with the
  endpoint it lived on, so `bob@localhost` now reaches epass, is answered like every other
  address, and the customer is told a link is on its way to an address that cannot receive one.

## Goals

- Each of the three decisions is available to a consumer through a supported API, with no
  hand-copied constants, no reimplemented formats and no reflection.
- Nothing here weakens what 008 established. In particular: no public ticket minting in the
  production surface, and no seam a consumer can use to turn sign-in into an account-enumeration
  oracle.
- justsmtp.com's three workarounds are deleted, not deprecated. As with 008 R12, a seam no
  consumer has used has been compiled, not tested.

## Non-goals

- **Revisiting the ticket format, the cookie posture, or the failure codes.** 009 adds seams
  around what 008 settled.
- **A general address-validation library.** The library will not learn what a good address looks
  like; it will learn how to ask the consumer.
- **Letting a consumer choose the failure code for a refused address.** See R6 — that is the
  enumeration oracle, and it is closed by construction rather than by documentation.
- **Publishing NativeWeb to a package feed**, still tracked separately.
- **Changing the epass wire protocol or `apps/web`.**

## User stories

- As an application author, I want to end a session from inside a request filter, so that an
  account disabled between sign-in and this request stops being signed in on this request.
- As an application author, I want to mint a session ticket in my integration tests using the
  library's own code, so that my tests break when the format changes instead of passing against a
  format nothing ships.
- As an application author, I want to refuse an address my product cannot serve before a login
  link is attempted, so that I do not promise a customer a message that cannot arrive.
- As a reviewer, I want a consumer's sign-out to use the same cookie attributes as its sign-in, so
  that the two cannot drift into a silent no-op.
- As an end user, I want to be told an address will not work rather than to wait for a link that
  never comes.

## Requirements

- **R1** A consumer can end the session from anywhere in a request, through the ASP.NET
  authentication stack rather than by writing a cookie.
- **R2** A session ended that way is byte-for-byte the deletion the library's own logout performs.
  One implementation, so the two cannot drift.
- **R3** A consumer's tests can mint a valid session ticket using the library's own minting code,
  without the production surface gaining a way to mint one.
- **R4** The test-minting seam can also produce a ticket that is expired, and one that is
  tampered with, so a consumer can assert refusal as well as acceptance.
- **R5** A consumer can refuse a submitted address before any provider call is attempted, and the
  refusal costs no provider round trip.
- **R6** A refused address is reported to the page with the library's existing address-unusable
  code and nothing else. The consumer supplies no text and chooses no code, so no refusal can
  distinguish an address that has an account from one that does not.
- **R7** All three seams are optional. A consumer that adopts none of them behaves exactly as it
  does under 008.
- **R8** Everything ships AOT-clean, under repo-wide `TreatWarningsAsErrors`.
- **R9** justsmtp.com uses all three seams, and its three workarounds no longer exist.
- **R10** Each seam is covered at unit and integration tier; the sign-out seam additionally at
  end-to-end, because a cookie deletion that the browser ignores is invisible below that tier.

## Acceptance criteria

- **R1** — Given a signed-in request, When consumer code calls `SignOutAsync` for the library's
  scheme, Then the response carries an expiring `Set-Cookie` for the session cookie and the next
  request is anonymous.
- **R2** — Given a sign-out through the authentication stack and one through the logout endpoint,
  When the two `Set-Cookie` headers are compared, Then they are identical but for nothing.
- **R3** — Given a test referencing only the library and its testing package, When it mints a
  ticket for a subject and presents it, Then the host authenticates that subject. Given the same
  test references only the production library, Then no minting API is reachable.
- **R4** — Given a ticket minted expired, Then it is refused. Given a ticket minted valid and then
  tampered with by one byte, Then it is refused, and the two refusals are distinguishable in the
  same way 008 R9 requires.
- **R5** — Given a consumer policy that refuses an address, When that address is submitted, Then
  the provider is never called and the response is the address-unusable redirect.
- **R6** — Given two addresses a consumer's policy refuses, one with a local account and one
  without, When each is submitted, Then the two responses are identical in status, location and
  header set.
- **R7** — Given a consumer that registers no policy and no sign-out call, When the 008 suite is
  run against it, Then every test passes unchanged.
- **R9** — Given justsmtp.com on this version, When its suite runs, Then it passes with
  `DashboardExtensions`'s hand-copied attributes, `SessionTests`'s hand-built ticket and the
  absence of an address check all gone.

## Open questions

None blocking. Three decisions were taken before drafting and are recorded so `/plan` does not
reopen them:

- **The sign-out seam is `IAuthenticationSignOutHandler`, not a public `SessionAuth`.** It is the
  framework's own answer to this exact question, it needs no new public type, and it keeps the
  cookie-writing code internal where 008 put it.
- **The test-minting seam lives in the existing `nativeweb.signin.epass.testing` package**, reached
  by `InternalsVisibleTo`. That package already exists for the flow-store conformance fixture and
  already carries an xunit reference, so it is the one place a consumer can be handed internals
  without an application's production closure acquiring them.
- **The address policy returns a boolean, not a failure code.** A consumer that could choose the
  code could choose a different one for an address it recognises, and that is an enumeration
  oracle built out of a feature. The bound is structural, not documentary.
