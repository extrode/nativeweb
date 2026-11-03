# 015 — sign-in: newest flow wins, stale callbacks can't kill it

Date: 2026-11-03. Tier: Standard. Paired with epass.io plan 013 (consent bound to the displayed request).

## Why

A downstream product's sign-in incident, 2026-11-03: the operator had to approve consent twice. The RP flow
(`FlowLifetime` 15 min) died while epass's login flow (30 min) was still live; the first callback
failed `state_mismatch` and the retry asked again. An audit of that product's sign-in found:

- `SignInEndpoints.GetCallbackAsync` clears the flow cookie before checking state, so a stale or
  forced cross-site `GET /callback?state=x` kills the newer pending flow (sign-in DoS).
- A missing cookie and a wrong state both report `state_mismatch`; an expired flow is never
  reported as `StateExpired`.
- `InMemoryLoginFlowStore` returns `AlreadyUsed` for a missing row instead of `Unknown`.
- 15 min `FlowLifetime` is shorter than the IdP's 30 min.

Desired behaviour (founder): starting a new sign-in invalidates the previous unfinished one; once a
sign-in succeeds, only the current request is served.

## Changes (libs/nativeweb.signin)

1. `GET /signin`: if a flow cookie already holds a state, consume that flow with
   `ILoginFlowStore.TakeAsync` (atomic; result ignored) before starting the new one. No interface
   change — ghosthive's `PostgresLoginFlowStore` keeps compiling.
2. `GET /callback`: clear the flow cookie only when the query state equals the cookie state. A
   callback whose state differs from the cookie leaves the cookie (and the newer flow) intact and
   returns `StateMismatch`.
3. Missing cookie: take the query state from the store; `Expired` → `StateExpired`, `Taken` (a flow
   started in this browser whose cookie was lost) → still refuse (no cookie binding = CSRF), report
   `StateMismatch`; never complete without the cookie. Matching cookie + `Expired` → `StateExpired`.
   `StateExpired` renders as a restart: redirect to `/signin` (one hop, not a loop: only when the
   expired flow's state matched the cookie). No returnTo: `FlowTakeResult.Expired()` carries no
   flow and the conformance suite pins that, so the restarted sign-in lands on the default return
   path. Carrying it would need a store-contract change (follow-up if it matters).
4. `InMemoryLoginFlowStore.TakeAsync` on a missing row → `Unknown`.
5. `SignInOptions.FlowLifetime` default 30 min.

Tests: unit tests per change in `tests/nativeweb.signin.tests.unit`, the conformance suite still
passes for the in-memory store; each test fails when its change is reverted.
