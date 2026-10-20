# 025 — How the permission layer names a subject

Status: done
Created: 2026-09-04

## Problem

`PermissionContext.HeldAsync` reads the subject from exactly one claim and offers no way to
change it. `libs/nativeweb.permissions/PermissionContext.cs:41` calls
`user.FindFirstValue(ClaimTypes.NameIdentifier)`, and `ClaimTypes.NameIdentifier` is the WS-Fed
URI `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`. Every permission in
the framework hangs off that one string: the store's `nw_subject.subject`, every grant row, every
group membership, and the `EnsureSubjectAsync` call that creates the local record.

A host whose authentication scheme surfaces the subject under any other name gets an empty
string, which the same method turns into the empty permission set, which
`PermissionEndpointFilter` turns into a refusal. The application is not misconfigured in any way
it can see: sign-in succeeds, the principal is authenticated, the claims are present, and every
gated route answers 403 with nothing written anywhere to say the subject was never found. The
generated admin is unreachable and the failure looks like a missing grant.

This is reachable from stock configuration rather than an exotic one. A JWT bearer scheme with
`MapInboundClaims = false` — the setting an OIDC-native application sets deliberately, so that
`sub` stays `sub` — produces precisely this state. So does any scheme whose subject is an email,
a tenant-scoped identifier, or a claim the host mints itself.

The cost is not confined to one application. `work/todo.md` records that every product in the
fleet is expected to build on NativeWeb, and each of them chooses its own authentication scheme.
A framework that silently refuses every request under a scheme it did not anticipate cannot be
adopted without first reading its source.

## Goals

- A host can tell the permission layer which claim names the subject, without replacing the
  shipped `IPermissionContext`.
- The default behaviour is unchanged for a host that configures nothing, so no existing
  application changes on upgrade.
- A request whose principal is authenticated but carries no subject under the configured claim
  is distinguishable, at runtime, from a request whose subject holds no permissions. The two are
  different faults and today they are the same 403.
- A host that wants a subject that is not any single claim — a composite, a lookup, a
  normalisation — can supply one.
- The subject a host supplies is the same string the store, the access admin and every grant row
  use, with no second mapping anywhere.

## Non-goals

- Authentication of any kind. Which claims exist, and whether they are trustworthy, is the
  scheme's business and epass.io's. This feature reads a principal that has already been
  authenticated.
- Claim transformation as a general facility. ASP.NET Core has `IClaimsTransformation` and a
  host that needs it should use it; this is about which claim the permission layer reads.
- Multi-tenancy, realm scoping, or a subject that varies by resource. A subject is one opaque
  string per principal.
- Changing the store seam. `IPermissionStore` keys on a subject string and continues to.

## User stories

- As an application author using JWT bearer with `MapInboundClaims = false`, I want the
  permission layer to read `sub`, so that my admin is reachable without me reimplementing
  `IPermissionContext`.
- As an application author whose provider identifies people by a tenant-qualified string, I want
  to compose the subject from two claims, so that two tenants' users cannot collide in
  `nw_subject`.
- As an operator debugging a 403, I want to see whether the subject was resolved at all, so that
  I can tell a missing grant from a misconfigured claim.
- As an author who configures nothing, I want today's behaviour, so that upgrading costs me
  nothing.

## Requirements

- R1 The permission layer exposes a configuration point that names the claim carrying the
  subject. Supplying nothing selects the current claim.
- R2 The permission layer accepts a host-supplied function from an authenticated principal to a
  subject string or to no subject, as an alternative to R1.
- R3 R1 and R2 are mutually exclusive; configuring both is a startup failure with a message
  naming both.
- R4 A principal that is authenticated and yields no subject is recorded at runtime, once per
  request, distinctly from a principal that yields a subject holding no permissions.
- R5 A principal that is not authenticated yields no subject and is not recorded as a fault.
- R6 An empty or whitespace-only subject is treated as no subject, whichever of R1 or R2
  produced it.
- R7 The configured subject reaches `EnsureSubjectAsync`, `GrantedAsync` and the access admin
  unchanged, with no normalisation the host did not ask for.
- R8 The documented behaviour is stated on the public surface a host configures, and in
  `libs/nativeweb.permissions/README.md`.

## Acceptance criteria

- Given a host that configures nothing, when a principal carries `ClaimTypes.NameIdentifier`,
  then the permissions resolved are the same as before this change. (R1)
- Given a host that names `sub`, when a principal carries `sub` and not
  `ClaimTypes.NameIdentifier`, then its grants resolve and its gated routes answer. (R1)
- Given a host that supplies a function, when the function returns a composed string, then that
  string is the one `EnsureSubjectAsync` receives. (R2, R7)
- Given a host that configures both a claim name and a function, when the application starts,
  then it fails with a message naming both. (R3)
- Given an authenticated principal carrying no subject under the configured claim, when a gated
  route is requested, then the request is refused **and** the absent subject is observable at
  runtime. (R4)
- Given an anonymous request, when a gated route is requested, then the refusal is recorded as a
  refusal and not as an absent subject. (R5)
- Given a function that returns `"   "`, when a gated route is requested, then the request is
  refused as having no subject rather than creating a whitespace row in `nw_subject`. (R6)

## Open questions

- None.

## Provenance

Finding from the `review-deep` (fable) pass on spec 024, 2026-09-04, recorded in `work/todo.md`.
The related finding on the same method — that `HeldAsync` memoises on the first principal and
ignores its `user` argument thereafter — is a defect rather than a design gap and is fixed
separately on `fix/024-permission-followups`.
