# 024 — Model permissions and groups
Status: draft
Created: 2026-09-03

## Problem

NativeWeb can generate a complete CRUD admin for a model at compile time, and it can sign a user
in against an external OIDC provider, but it has nothing in between. The only way to say who may
do what is a single role name checked against a claim and applied to a whole model. An
application that wants an editor who may write articles but not delete them, or a moderator who
may touch comments and nothing else, has to hand-write that logic in every endpoint and then
hand-write it a second time to decide what the admin navigation should show. Nothing keeps the
two in step.

Django solves this with `contrib.auth`: every model has four permissions by existing, users
belong to groups, groups carry permissions, and the admin reads the same permissions the
application code reads. That is the shape NativeWeb is missing, and it sits inside the framework
boundary this repo has already drawn, since Django ships `contrib.auth` and leaves the OAuth2 and
OIDC server to a third-party package. Identity stays with epass.io. What is absent here is the
app-local half: which of the people epass.io vouches for may do which things inside this one
application.

The gap is felt twice. An application author writes and maintains ad-hoc checks. And the
generated admin, which is the framework's most complete feature, is all-or-nothing: a user either
administers everything or nothing at all.

## Goals

- A model author declares a permission set in one attribute, and gets a permission per CRUD
  operation without naming any of them.
- The generated admin enforces those permissions per operation with no further declaration, so a
  user who may not delete sees no delete control and cannot reach the route.
- The same permission is checkable from a hand-written endpoint through one injected call.
- Permissions resolve for a user identified only by an opaque subject string from an external
  provider, with no local password, credential or identity record beyond that string.
- Users can be grouped, and a permission granted to a group reaches every member.
- Permission names are stable compile-time constants, so a renamed model or a removed permission
  is a build error rather than a silent grant.
- Two assemblies that each declare a model of the same name produce distinct permission names.
- An application with no permission declarations behaves as it does today.

## Non-goals

- Any identity concern: authentication, credentials, passkeys, sessions, token issue, consent,
  realm ownership. epass.io owns those and this feature reads a subject claim.
- A policy language, a relationship graph, row-level or attribute-based rules. Permissions are
  per model and per operation, plus author-named extras.
- Any wire protocol, HTTP endpoint or out-of-process decision service.
- NuGet packaging, plugin discovery, or cross-assembly model discovery.
- Replacing or deprecating the existing role and policy attribute. It keeps working unchanged.
- A user-facing sign-up, profile or account management surface.
- Storing user display names, email addresses or any profile field the provider already holds.

## User stories

- As a model author, I want a model to have permissions by declaring one attribute, so that I do
  not name four strings per model and keep them in step with the model by hand.
- As an application author, I want the generated admin to respect those permissions per
  operation, so that I can give someone edit rights without giving them delete rights.
- As an application author, I want to check the same permission in a hand-written endpoint, so
  that a rule is stated once and enforced everywhere.
- As an operator, I want to put a person in a group and have their access follow, so that I do
  not grant permissions one at a time.
- As an operator, I want a person's access to survive them changing their name or email at the
  identity provider, so that administration does not break when a profile changes.
- As a model author, I want a permission I no longer declare to stop compiling where it is used,
  so that removing a capability cannot leave a live grant behind.
- As an application author with no permission declarations, I want nothing about my application
  to change, so that adopting this is a choice.

## Requirements

- R1 A model marked as having permissions produces four permissions covering view, add, change
  and delete, named from the model, without the author naming any of them.
- R2 A model author can declare additional permissions by name on the same declaration, and those
  appear alongside the four.
- R3 Every permission is available to application code as a compile-time constant whose
  identifier derives from the model name and the permission name.
- R4 Permission names are scoped per assembly, and the scope is settable once per assembly. Two
  assemblies declaring identically named models produce distinct permission names.
- R5 A permission decision is available through a single injected call taking the current
  principal, a permission and an optional resource identity, and returning allowed or not.
- R6 A subject is identified by the opaque subject claim from the external provider. A local
  record is created on first sight of a subject and holds no credential and no profile field.
- R7 A permission may be granted to a subject directly or to a group the subject belongs to, and
  the decision allows if either grants it.
- R8 A model that has permissions and a generated admin has each generated route gated by the
  matching permission with no further declaration by the author.
- R9 An author can exempt the read operations of a scaffolded model from any check, so that a
  model's list and detail pages are reachable by anyone.
- R10 An author can require a role in addition to the permission for a given operation. Both must
  hold.
- R11 The existing role and policy attribute continues to accept role and policy names only. A
  permission name is never a valid value for it.
- R12 The generated admin omits from its own navigation and from each record's controls any
  operation the current subject is not permitted to perform.
- R13 An operator can create groups, put subjects in groups, and grant permissions to subjects and
  groups, through the generated admin.
- R14 A permission that is no longer declared cannot be granted, and any code referring to it
  fails to compile.
- R15 An application that declares no permissions is unaffected in behaviour, routing and
  generated output.
- R16 The permission layer functions for a model that has no generated admin.
- R17 The permission layer does not depend on the sign-in library beyond the subject string, and
  functions when a subject is supplied by other means.

## Acceptance criteria

- Given a model declared with permissions and no extras, when the application builds, then four
  permission constants exist for it and are addressable by model name and operation. (R1, R3)
- Given a model declaring one extra permission, when the application builds, then five constants
  exist for it. (R2)
- Given two assemblies each declaring a model of the same name, when both build, then their
  permission names differ. (R4)
- Given a subject granted the change permission for a model and not the delete permission, when
  they open that model's admin list, then edit controls are present and delete controls are
  absent; and when they request the delete route directly, then the request is refused. (R8, R12)
- Given the same subject, when application code asks whether they may change and whether they may
  delete, then the answers are yes and no respectively. (R5)
- Given a subject in a group holding the delete permission and holding no permission directly,
  when they request the delete route, then the request succeeds. (R7)
- Given a subject presenting a claim set whose only stable element is the subject string, when
  they are seen for the first time, then a local record exists for them and holds no credential
  and no profile field. (R6)
- Given the same subject presenting a different display name on a later request, when their
  permissions are evaluated, then the outcome is unchanged. (R6)
- Given a scaffolded model whose read operations are exempted, when an unauthenticated visitor
  requests its list page, then the page is served. (R9)
- Given an operation requiring both a role and a permission, when a subject holds one and not the
  other, then the request is refused in both directions. (R10)
- Given a permission name supplied to the existing role and policy attribute, when the application
  builds, then it is not treated as a permission. (R11)
- Given an operator using the admin, when they create a group, add a subject to it and grant it a
  permission, then that subject's decisions reflect the grant. (R13)
- Given a declared permission that is then removed from the declaration, when the application
  builds, then code referring to its constant fails to compile. (R14)
- Given an application declaring no permissions, when it builds and runs, then its routes,
  generated output and behaviour are identical to before this feature. (R15)
- Given a model with permissions and no admin, when the application builds, then its permission
  constants exist and decisions about them resolve. (R16)
- Given an application that does not use the sign-in library, when a subject string is supplied by
  other means, then decisions resolve. (R17)

## Settled design decisions

Recorded because they were decided before this spec was written, by a three-model consultation
(fable, glm-5.3, qwen3.8-max) on 2026-09-03, and re-deciding them would be the expensive kind of
forgetting. The reasoning belongs in `024-plan.md`; only the outcomes are listed here.

- Two class-level attributes, not one. Declaring a permission set and gating a route are separate
  concerns, and a model may declare permissions with no admin at all.
- The declaration attribute is named for the set it declares, as a plural noun.
- The existing gate attribute keeps holding role and policy names only. Permission names are never
  valid in its slots, because a slot that sometimes holds a role and sometimes a permission cannot
  be read without knowing which.
- The permission check is the floor and the gate attribute composes on top of it with AND, so the
  common case declares nothing and the exception adds rather than replaces.
- Read exemption is expressed as a typed flag, not as a sentinel string in a slot documented to
  hold role names.
- Permission naming scope is set at the assembly level. Turning permission enforcement on or off
  is not, because a switch that changes every model's access posture from outside the model makes
  the model unreadable on its own.
- Human labels for permissions are derived from the model's display name and the permission name
  rather than supplied by the author.

## Open questions

None.
