# 026 — Deleting a group, forgetting a subject

Status: done
Created: 2026-09-04

## Problem

The permission layer can create a group and it can never remove one. `IPermissionStore`
(`libs/nativeweb.permissions/IPermissionStore.cs`) declares fourteen operations and none of them
deletes a group or forgets a subject: `CreateGroupAsync`, `AddMemberAsync`, `RemoveMemberAsync`,
four grant and revoke methods, and five readers. `MapAccessAdmin` exposes what the store exposes,
so the access screens have no delete control either.

Three consequences follow, and the first is the one an operator meets.

**A typo is permanent.** An operator who creates `Editrs` instead of `Editors` has added a row to
`nw_group` that nothing in the framework can remove. The group appears in the admin's group list
for the life of the application. The only remedy is hand-written SQL against the product's
database, which is precisely the situation the access admin exists to avoid, and on the in-memory
store there is no remedy at all.

**The schema already anticipates the operation that cannot happen.** Every dependent table in
`db/sample/migrations/0006_permissions.sql` carries `ON DELETE CASCADE` — `nw_group_member` and
`nw_group_grant` on `nw_group`, `nw_group_member` and `nw_subject_grant` on `nw_subject`. Four
cascade clauses exist to clean up after a delete that no code path performs. They were written
for an operation the seam does not have.

**The error type documents a state nothing can produce.** `PermissionConflictException` cites "a
group that has since been deleted" as the condition it reports. Since 024's fix that exception is
raised across both stores for a group that never existed, but the deletion it names remains
unreachable, so the documented cause is still fictional.

Forgetting a subject is the same gap on the other axis. `EnsureSubjectAsync` is the only way a
subject enters `nw_subject` and there is no way out. A subject accumulates on first sight of any
principal, so the table grows monotonically with every person who has ever signed in, including
people who have since left. That is a retention question as well as a tidiness one: the subject
string is an identifier for a person, and a framework that cannot delete it hands every product
built on it a data-protection obligation it cannot discharge.

## Goals

- An operator can remove a group they created by mistake, through the access admin, without SQL.
- An operator can remove a subject's local record and every grant and membership attached to it.
- Both operations behave identically on the two shipped stores, in line with the seam contract
  024 established.
- A destructive operation in the access admin is distinguishable from a non-destructive one
  before it is performed.
- The `ON DELETE CASCADE` clauses in the migration become reachable, and their behaviour is
  asserted rather than assumed.

## Non-goals

- Soft delete, archival, an undo, or a trash view. A delete removes rows.
- An audit trail of who deleted what. Worth having and not this feature; the framework has no
  audit facility for any other operation either.
- Deleting a permission. Permissions are compile-time constants and a permission stops existing
  by not being declared, which is already a build error at every call site.
- Any bulk or cascading operation across models, and any scheduled or automatic expiry.
- Deleting the person. This forgets a subject's authorization record inside one application;
  identity is epass.io's.

## User stories

- As an operator who mistyped a group name, I want to delete the group, so that the access
  screens show the groups that exist rather than my typo.
- As an operator winding down a team, I want to delete its group, so that its members lose the
  permissions the group carried without my revoking each one.
- As an operator handling a departure, I want to forget a subject, so that no grant or membership
  outlives the person's access.
- As an operator about to delete, I want to see what the deletion takes with it, so that I do not
  discover the members and grants afterwards.
- As a framework author, I want one contract for both stores, so that a test passing against the
  in-memory store means something about PostgreSQL.

## Requirements

- R1 `IPermissionStore` gains a group deletion that removes the group, its memberships and its
  group grants.
- R2 `IPermissionStore` gains a subject deletion that removes the subject record, its
  memberships and its subject grants.
- R3 Both operations are idempotent in the sense 024 settled for the rest of the seam: deleting
  something the store does not hold raises `PermissionConflictException` on both stores, not a
  silent success on one and a database error on the other.
- R4 Both shipped stores implement both operations with the same observable behaviour for the
  same inputs, including the empty-argument and not-found cases.
- R5 The PostgreSQL implementation relies on the existing `ON DELETE CASCADE` clauses rather than
  deleting dependent rows by hand, and a test asserts the dependents are gone.
- R6 The access admin exposes both operations, each behind the antiforgery metadata every other
  post on those screens carries.
- R7 A delete control states what the deletion removes, with the counts the store can supply,
  before it is submitted.
- R8 A delete on a group or subject the store no longer holds answers 409, the way every other
  stale post on those screens does since 024.
- R9 Deleting a group revokes its permissions from its members with no further call, and the
  members' own direct grants are untouched.
- R10 Deleting a subject does not delete the groups it belonged to.
- R11 A subject deleted and then seen again by `EnsureSubjectAsync` is a new record with no
  grants, not a restoration.

## Acceptance criteria

- Given a group with two members and three grants, when it is deleted, then the group, both
  memberships and all three grants are gone from both stores. (R1, R4, R5)
- Given a member of that group who also held one direct grant, when the group is deleted, then
  the member holds that one grant and nothing the group carried. (R9)
- Given a subject in two groups with one direct grant, when it is forgotten, then both
  memberships and the grant are gone and both groups still exist with their other members. (R2,
  R10)
- Given a group identifier the store does not hold, when a delete is attempted, then both stores
  raise `PermissionConflictException`. (R3, R4)
- Given a group deleted between the page rendering and the post, when the delete is submitted,
  then the response is 409. (R8)
- Given the delete control on a group with two members, when it is rendered, then it states that
  two memberships will be removed. (R7)
- Given a post to either delete route with no antiforgery token, when it is submitted, then it is
  refused. (R6)
- Given a forgotten subject that signs in again, when a gated route is requested, then it holds
  no permissions. (R11)

## Open questions

- None.

## Provenance

Finding from the `review-deep` (fable) pass on spec 024, 2026-09-04, recorded in `work/todo.md`.
