# 016 — OAuth relying party: the client half, as framework

Status: done
Created: 2026-09-01

## Problem

Every product that wants "sign in with an account the user already has" writes the client half of
OAuth itself. Three have now done so, against the same identity provider, three different ways:

| product | what it wrote |
|---|---|
| `ghosthive.com/apps/web` | authorization-code + PKCE, code exchange, userinfo, then an opaque HttpOnly `SameSite=Lax` session cookie over a server-side token map |
| `ghosthive.com/apps/control-plane` | bearer-token introspection with a 30-second TTL cache, as an endpoint filter |
| `justsmtp.com/apps/web` | a call into a NativeWeb library that no longer exists |

None of that code is any of those products'. It is redirect construction, a PKCE verifier, a state
parameter, a single-use flow record, an open-redirect sanitiser, a session cookie, a refresh path
and an authentication handler — identical in every product, and the parts most worth getting right
are the parts a reimplementation drops. An open-redirect sanitiser is twenty lines that everyone
writes with a different hole in it.

**This repository already proved the point and then lost the proof.** Spec 008 extracted exactly
this plumbing into a library and migrated justsmtp onto it: 1,329 lines across nine files became
543 across six. Spec 009 then found three seams the extraction had closed over, each of which had
forced a consumer workaround that a spec was needed to remove. Both libraries left on 2026-08-31
with the identity provider ([decision 007](../../decisions/2026-08-31-007-idp-leaves-nativeweb.md)),
because they were named for a product and shaped around that product's flow. The generic
three-quarters left with them, and `justsmtp.com` has not compiled since.

Meanwhile a stamped product cannot put anything behind a login at all. `tools/new-product.ps1`
registers no authentication scheme, so `RequireAuthorization` on a scaffolded admin route returns
401 to everybody until the product author wires a scheme by hand.

Django ships `contrib.auth` and the ecosystem ships `django-allauth`; the OAuth **server** is a
separate package and always was. NativeWeb ships bot traps. A framework whose every consumer
hand-rolls the client half of authentication is a template with ambitions.

## Goals

- A product signs users in against an external provider by supplying **configuration and one
  callback** — target under 15 lines of wiring, against roughly 400 lines per product today.
- `ghosthive.com` can migrate `apps/web` without carrying its own PKCE client, and can put its
  control-plane bearer check behind a supported filter rather than a hand-rolled one.
- `justsmtp.com` has something to reference in place of the library that left.
- A stamped product has a working authentication scheme the moment it is stamped, so a scaffolded
  `[Auth]` route authenticates instead of universally refusing.
- **Nothing in this repository knows what epass is.** A conforming provider is configuration.

## Non-goals

Each of these is out of scope for a stated reason, not an omission.

- **Issuing anything.** No authorization endpoint, no token endpoint, no consent screen, no
  refresh-token rotation *as a server*, no client registry, no realms, no SSO modes, no identity
  schema. NativeWeb is the client and never the server. This is the line
  [`AGENTS.md`](../../../AGENTS.md) draws and decision 007 enforced; 016 does not move it.
- **Any provider-specific grant.** epass's primary sign-in is a passwordless email link over a
  custom `urn:epass:grant:login-link` grant, which is not a registered grant type. A custom grant
  belongs to whoever defined it. **Consequence, stated rather than discovered:** a product that
  needs that flow layers a provider-specific package on top of 016 — probably shipped by epass.io
  — and 016 must leave room for that without knowing about it.
- **ID token validation, JWKS, and OIDC discovery.** Deferred by fact, not preference: the only
  provider any consumer uses issues **opaque access tokens and no `id_token`**, serves
  `/.well-known/oauth-authorization-server` (RFC 8414) rather than `openid-configuration`, and
  publishes no JWKS. Building signature validation now would verify a token nothing sends, while
  pulling a heavy dependency through a NativeAOT build gate. Identity comes from the userinfo
  response. If a provider that issues signed tokens appears, that is its own spec.
- **A user model, account storage, or profile management.** 016 learns who the provider says the
  user is and hands that to the product. What a product stores about a person is the product's.
- **Password, passphrase or credential handling of any kind.** Those primitives already exist and
  are not part of a relying party.
- **Multi-factor, step-up, or authentication-context reasoning.** Requires assertions the provider
  does not make today.
- **Publishing NativeWeb to a package feed.** Tracked separately as G1.

## User stories

- As a product author, I want to sign a user in against a configured provider without writing the
  OAuth exchange, so that my first login costs configuration rather than a security review.
- As a product author, I want a stamped product to authenticate out of the box, so that a
  scaffolded admin route is reachable by an administrator rather than by nobody.
- As a product author, I want to end a session from inside a request filter, so that an account
  disabled between sign-in and this request stops being signed in on this request.
- As a product author, I want to mint a session in my integration tests using the framework's own
  code, so that my tests break when the format changes instead of passing against a format nothing
  ships.
- As an API author, I want to accept a provider-issued bearer token on an endpoint without calling
  the provider on every request, so that a hot endpoint is not gated by a network round trip.
- As a security reviewer, I want one implementation of the redirect sanitiser, the state check and
  the cookie attributes, so that there is one thing to review rather than one per product.
- As an operator, I want a stolen database to contain no usable session credential, so that a
  backup is not a set of live logins.
- As an end user, I want to be returned to the page I was trying to reach, and never to a site I
  did not ask for.

## Requirements

**The sign-in exchange**

- **R1** A product can complete the authorization-code grant with PKCE against a configured
  provider without writing the redirect, the code challenge, the exchange, or the parsing of
  either response shape.
- **R2** Every authorization request carries a cryptographically random `state` and a PKCE
  verifier, both bound to a single sign-in attempt, and a callback whose `state` does not match a
  live attempt is refused.
- **R3** A sign-in attempt is usable **at most once**. A replayed callback is refused even when
  every value in it is otherwise valid.
- **R4** Abandoned sign-in attempts expire and are removed without operator action.
- **R5** The post-login destination is confined to the application's own origin. A destination
  naming any other host is discarded and replaced with a safe default, and no configuration makes
  an external destination reachable.
- **R6** The identity a product receives comes from the provider's userinfo response and carries at
  minimum a stable subject identifier. The documentation states that a subject identifier may be
  **pairwise** — different for the same person at a different relying party, and not necessarily
  stable across a change of provider configuration — so a product storing it as a user key knows
  what it is storing. This is not hypothetical: the provider every consumer uses derives the
  subject per-application by default, and a provider-side mode change re-derives every subject in
  the affected group.

**The session**

- **R7** A signed-in user is identified by an opaque value that carries no user data and is not
  derived from any user data.
- **R8** No value ever sent to a client is recoverable by reading the session store, its backups,
  or its replication stream. (This is 007's finding, and 016 must not reintroduce what 007 closed.)
- **R9** The session cookie is host-scoped, `HttpOnly`, `Secure`, and `SameSite` at a setting that
  survives the provider redirect.
- **R10** The session store is an interface a product implements. The framework ships at least one
  working implementation sufficient for tests and for a single-instance deployment, and ships no
  schema and no migration.
- **R11** Registration adds a real authentication scheme, so `RequireAuthorization` and
  `[Authorize]` work on any endpoint including a scaffolded one, without further wiring.
- **R12** A product can end a session from anywhere in a request through the authentication stack,
  not by writing a cookie. The resulting cookie deletion is byte-for-byte the one the framework's
  own sign-out performs — one implementation, so the two cannot drift.
- **R13** Access tokens are refreshed before expiry without the user being redirected, and a
  refresh the provider refuses ends the session rather than leaving a session holding a dead token.

**Bearer tokens for APIs**

- **R14** A product can require a valid provider-issued bearer token on an endpoint, verified by
  asking the provider, without writing the introspection call.
- **R15** Introspection results are cached for a configurable, short duration, and the cache never
  extends a token's life beyond what the provider reported.
- **R16** A refused bearer token yields an authorization failure carrying no detail about why, so
  that no response distinguishes an unknown token from a revoked or expired one.

**Provider neutrality**

- **R17** Endpoint locations, client identity, scopes and redirect path are configuration. No
  provider name, host, path or vendor-specific field appears in any type name, namespace or
  default value.
- **R18** More than one provider can be configured and used at once, distinguished by name.
- **R19** A misconfiguration is refused at startup with a message naming the missing or invalid
  setting, rather than producing a failure at the first sign-in attempt.
- **R30** A product can present client credentials at the token endpoint by HTTP Basic, by form
  fields, or not at all where the provider treats the client as public and relies on PKCE. Which of
  the three is used is configuration.
- **R31** Fields a provider returns that the framework does not model are ignored without error.
  A provider is free to extend any response, and a relying party that fails on an unrecognised
  field breaks the first time its provider ships a feature.

**Seams, so 009's findings are not rediscovered**

- **R20** A product's tests can mint a valid session using the framework's own minting code,
  without the production surface gaining any way to mint one.
- **R21** The test seam can also produce a session that is expired and one that is tampered with,
  so a product can assert refusal as well as acceptance.
- **R22** A product can refuse a sign-in attempt on its own policy before any provider call is
  made, and the refusal costs no provider round trip.
- **R23** A refusal under R22 is reported identically regardless of whether the subject has a local
  account, so no refusal becomes an account-enumeration oracle.
- **R24** Every seam is optional. A product that adopts none behaves as if they did not exist.

**Delivery**

- **R25** Everything ships AOT-clean under repo-wide `TreatWarningsAsErrors`, with no trim or AOT
  analyzer warnings.
- **R26** Coverage at all three tiers: unit; integration through a real host against a stub
  provider; end-to-end through a browser for the redirect round trip, the session cookie and
  sign-out — because a cookie the browser ignores is invisible below that tier.
- **R27** A stub provider ships with the tests, so the suite runs with no external service and no
  network.
- **R28** The reference application demonstrates a signed-in page and a signed-out page, and the
  stamped skeleton registers the scheme.
- **R29** The library documents itself with a README covering configuration, the wiring, the seams,
  and what a product must supply.

## Acceptance criteria

- **R1** — Given a configured provider and a stub issuing a code, When a user completes the round
  trip, Then the product's callback receives a subject identifier and the user is signed in, with
  no OAuth code in the product.
- **R2** — Given a callback whose `state` matches no live attempt, Then it is refused and no token
  request is made. Given a callback whose verifier does not match the challenge sent, Then the
  provider's refusal ends the attempt without a session.
- **R3** — Given a callback that completed successfully, When the identical callback URL is
  requested again, Then it is refused and no second session exists.
- **R4** — Given an attempt older than its configured lifetime, When the sweep runs, Then the
  record is gone and a callback presenting it is refused.
- **R5** — Given a return destination of `https://evil.test/x`, `//evil.test/x`, `/\evil.test`, or
  a scheme-relative or backslash variant, When sign-in completes, Then the user lands on the safe
  default and never on `evil.test`.
- **R6** — Given a userinfo response, Then the product receives the subject; and given a provider
  documented as issuing per-client subjects, Then the README states that the subject is not
  portable between clients.
- **R7/R8** — Given a live session, When the session store's entire contents are dumped, Then no
  row contains the cookie value, and no transformation of a row yields it.
- **R9** — Given a sign-in response, Then the `Set-Cookie` carries the host prefix, `HttpOnly`,
  `Secure`, and a `SameSite` value under which the provider redirect still arrives authenticated.
- **R10** — Given a product supplying only the interface implementation, Then it signs in. Given
  the shipped implementation, Then the suite passes with no database.
- **R11** — Given a scaffolded model marked for authorization in a freshly stamped product, When
  an unauthenticated request arrives, Then it is challenged and redirected to sign-in; and when an
  authenticated one arrives, Then it is served. Today the first is a bare 401 and the second is
  impossible.
- **R12** — Given a sign-out through the authentication stack and one through the sign-out
  endpoint, When the two `Set-Cookie` headers are compared, Then they are identical.
- **R13** — Given an access token within the refresh window, When a request arrives, Then it is
  refreshed with no redirect and the user does not notice. Given a provider that refuses the
  refresh, Then the next request is anonymous rather than carrying a dead token.
- **R14/R15** — Given a valid bearer token, When two requests arrive inside the cache window, Then
  the provider is called once. Given a token whose reported expiry falls inside the window, Then it
  is not served from cache past that expiry.
- **R16** — Given an unknown token, a revoked token and an expired token, When each is presented,
  Then the three responses are identical in status, body and header set.
- **R17** — Given a search of the shipped source for the provider's name and hostname, Then there
  are no matches outside test fixtures and documentation examples. This is asserted by a test.
- **R18** — Given two configured providers, When a user signs in through each, Then the two produce
  distinct schemes and neither session authenticates against the other.
- **R19** — Given a missing client identifier, an absent token endpoint, or a redirect path that is
  not application-relative, When the host starts, Then startup fails naming the setting.
- **R20** — Given a test referencing the framework and its testing package, When it mints a session
  for a subject, Then the host authenticates that subject. Given a project referencing only the
  production library, Then no minting API is reachable.
- **R21** — Given a session minted expired, Then it is refused. Given one minted valid and altered
  by one byte, Then it is refused, and the two refusals are distinguishable to the product.
- **R22/R23** — Given a product policy that refuses a subject, When sign-in is attempted, Then the
  provider is never called; and given two refused subjects, one with a local account and one
  without, Then the two responses are identical in status, location and header set.
- **R24** — Given a product that registers no policy, no custom store and no sign-out call, When
  the suite runs, Then every test passes.
- **R25** — Given `dotnet publish -p:PublishAot=true`, Then it completes with zero warnings.
- **R26/R27** — Given a machine with no network access to any identity provider, When the full
  suite runs, Then all three tiers pass.
- **R28** — Given a freshly stamped product, When it is run, Then a protected page challenges and a
  public page does not.
- **R30** — Given each of the three client authentication settings, When a token request is made,
  Then credentials appear in the Authorization header, in the form body, or nowhere, matching the
  setting and nothing else.
- **R31** — Given a token, userinfo or introspection response carrying fields the framework does
  not model, When it is parsed, Then it succeeds and the unmodelled fields are absent from the
  result rather than causing a failure.

## Open questions

None. Two were resolved before this spec was written: the starting point is the recovered library
from `f743436` with the provider-specific half removed rather than a fresh build, and ID token
validation is out of scope because the only provider in use issues none.
