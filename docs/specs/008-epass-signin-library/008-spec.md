# 008 — Sign-in library for epass.io relying parties
Status: done — closed 2026-09-02. R1, R11 and R12 were amended to describe what shipped; see
"Closed" below. Everything else was met and evidenced on 2026-08-25.
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

- **R1** A consumer obtains working sign-in by supplying a configuration section, one
  implementation of the login-flow storage contract, and one callback resolving a verified
  external subject to a local user. It writes no endpoint, no PKCE, no cookie handling, no callback
  logic and no configuration reading. *(Amended 2026-09-02: "supplying configuration" became
  "supplying a configuration section", and reading it by hand is now something the consumer does
  not write.)*
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
  markup, and asset URLs. The cookie name has a library-supplied default, `__Host-signin`, which the
  consumer may override; the rest acquire none. *(Amended 2026-09-02: was "None acquires a
  library-supplied default".)*
- **R12** justsmtp.com uses the library, and its nine `Auth/` files no longer exist. Sign-in
  behaviour there is unchanged — same URLs, same cookie, same outcomes — with two deliberate
  exceptions: every existing session is signed out once at cutover by the `v2` ticket format, and
  the login POST answers with a redirect to `?sent=1` on a page that no longer shows the address.
  *(Amended 2026-09-02: was "unchanged" without the exceptions.)*
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
  and returns, Then every outcome matches today's behaviour apart from the two exceptions R12
  names, and the nine files are gone from the repository.

## Acceptance review — 2026-08-25

All nineteen tasks are `[x]` and R12's other half landed: justsmtp.com is on the library
(`apps/web/justsmtp.web.csproj:79`), `eb06a3a` deleted eight of the nine `Auth/` files and rewrote
the ninth, and it is merged into that repository's `dev`. So the review that this spec's own
`Status` was waiting on can now happen — and it does **not** end in `done`.

**Three criteria are not met as written.** None is a defect in the shipped code: each is a place
where the code took a deliberate, documented decision that the criterion, written before the code
existed, does not describe. Each therefore needs the user to amend the criterion or change the
code, and none can be resolved by whoever reads this next.

| R | Verdict | Evidence |
|---|---|---|
| R1 | **Amend or change** — see (a) | `AddEpassSignIn` + 3 builder calls + `MapEpassSignIn` is 5 lines; justsmtp's real block is **32 non-comment lines** (`apps/web/Program.cs:128-190, 223`) |
| R2 | Met | `SignInEndpoints.cs`; `LoginPath`/`CallbackPath`/`LogoutPath`/`SignedOutPath` all consumer-set |
| R3 | Met | `Take.sql` is one `UPDATE … WHERE used_at IS NULL AND expires_at > now() … RETURNING`; conformance fixture; 4 tests fail when either predicate is dropped |
| R4 | Met | `ILoginFlowStore.cs` names no table and no query type; justsmtp owns `LoginFlowStore.cs` and its schema |
| R5 | Met | `EpassResult<T>` Ok/Refused/Unavailable, reachable only through `Match` |
| R6 | Met | unconfigured returns `Unavailable` from every `EpassClient` call rather than throwing |
| R7 | Met | provider `error`/`error_description` are internal; integration tier asserts the rendered failure carries none |
| R8 | Met | `ReturnPath.cs` + callback-side sanitising, not reachable as an opt-out helper |
| R9 | Met | `v2` base64url fields, constant-time verify, `SessionReadResult` Valid/Malformed/BadSignature/Expired |
| R10 | Met | `nativeweb.signin.epass.csproj:41` — one `ProjectReference`, to `nativeweb.security`; `nativeweb.auth` absent from the graph and from every source file |
| R11 | **Amend or change** — see (b) | `CookieName` has a library-supplied default |
| R12 | **Amend or change** — see (c) | migration landed; "no behavioural change" did not |
| R13 | Met | AOT gate: exit 0, zero IL2026/IL3050, 16,786,944 B |
| R14 | Met | named for the provider; README states no standards conformance |
| R15 | Met | 184 unit (re-run 2026-08-25) / 32 integration against real PostgreSQL / 3 Playwright e2e |

**(a) R1 says "under 10 lines" and the real wiring is 32.** Five of those lines are the library's
contract; the other 27 are the consumer reading `IConfiguration` by hand and assigning fourteen
options. That is a direct consequence of `AddEpassSignIn` having no `IConfigurationSection`
parameter — deviation 2 in `~/.claude/away-questions.md`, assumed rather than asked on 2026-08-19
and never answered, on the reasoning that R13 forbids `Bind()` so the section would be read
property-by-property anyway. Either the criterion counts only the contract lines, or the library
grows the section parameter it was planned with.

**(b) R11 says "None acquires a library-supplied default" and `CookieName` has one.** Flagged by
the T18 review at the time and left, because the default is good. One of the two is wrong.

**(c) R12 says "no behavioural change to sign-in as experienced by a justsmtp user", and there are
two, both deliberate.** First, the `v1` → `v2` ticket format signs every existing justsmtp user
out **once** at cutover — the fix for a subject containing `.` being read as `Malformed`, which
fails closed but is a lockout. Second, justsmtp's login POST used to re-render the form as a
**400** with the typo echoed and then show a "check your email" page **displaying the address**;
the library owns `POST {LoginPath}` and answers with a 302 to `?sent=1`, and justsmtp's
replacement page renders it **without the address on purpose** — "the library preserves that by
never looking. So does this" (`justsmtp.com/apps/web/Auth/LoginPage.cs:36`). The privacy change is
an improvement; it is still a behavioural change, and R12 as written forbids it.

**Also still unanswered from T18** (`~/.claude/away-questions.md`, 2026-08-19), none of them
blocking this spec but all four cheaper now than after a second consumer: a composed public
`BeginLoginAsync` so a consumer can own the POST and its rendering (which is what would let R12
be met literally); renaming `OnSignedIn` to `WithResolver`, since the delegate *decides* the
sign-in and vetoes by returning null rather than merely observing it; splitting `LoginPagePath`
out of `LoginPath`, which the plan specified and the code merged; and rejecting a non-loopback
`http://` BaseUrl, left open because it would break a deployment behind a TLS-terminating proxy.

## Closed — 2026-09-02

The three "amend or change" verdicts above were resolved by amending the criteria, since in each
case the shipped code had taken the better decision and the criterion predated it.

| R | Was | Now |
|---|---|---|
| R1 | "under 10 lines" against a 32-line hand-rolled `IConfiguration` block | **Met.** The section overload the review said was missing exists: `SignInConfiguration.AddSignIn(IServiceCollection, IConfiguration)` (`libs/nativeweb.signin/SignInConfiguration.cs:34`), added by spec 016. `skeleton/apps/__product__/Program.cs:46-50` wires sign-in in three lines plus `app.MapSignIn()`. |
| R11 | "None acquires a library-supplied default" vs `CookieName` | **Met** as amended. `SignInOptions.CookieName` defaults to `__Host-signin` (`libs/nativeweb.signin/SignInOptions.cs:142`) and the validator refuses any name without the `__Host-` prefix, so the default is the safe one and a consumer that overrides it cannot choose a weaker shape. |
| R12 | "no behavioural change" vs the one-time sign-out and the address no longer shown | **Met** as amended. Both differences are named in the requirement now; the one-time sign-out is the fail-closed fix for a `.` in the subject, and not echoing the address is the privacy posture the library holds by never reading it. |

The four T18 design calls listed under the review stay open in `~/.claude/away-questions.md`;
none was a criterion here. The library has since been renamed to `libs/nativeweb.signin` and the
identity provider has left the repository (`docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`),
so the paths in the review table are historical.

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
