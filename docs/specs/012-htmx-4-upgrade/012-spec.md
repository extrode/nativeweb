# 012 — htmx 4.0 upgrade
Status: draft
Created: 2026-08-29

## Problem

NativeWeb pins htmx at 2.0.10, vendored into both the product and the reference skeleton. htmx
4.0.0 was released on 2026-08-28. It is not a version bump: the library was rewritten from
`XMLHttpRequest` onto `fetch()`, every event was renamed to a `htmx:phase:action` scheme, the
payload those events carry was reshaped, attribute inheritance became opt-in, and error responses
now swap into the target by default where they previously did nothing.

Three things in this repo sit directly on top of the parts that changed. The progressive
enhancement layer listens to five htmx events and reads their payloads; its comments record
behaviours of htmx 2.0.10 it depends on, one of which says in as many words to re-verify on any
version bump. The antiforgery glue that puts a token on every enhanced request is a listener on a
renamed event, writing to a payload field that no longer exists in the same shape. And the
skeleton is a template other projects are meant to copy, so whatever it ships is what they
inherit.

Staying on 2.x is viable — it keeps the `latest` tag until early 2027 and continues to receive
security fixes — but the gap only widens, the migration cost does not fall, and a reference
implementation that ships a superseded major is a poor reference. The architecture doc has
carried a written upgrade checklist since the 4.0 beta; this is that checklist coming due.

## Goals

- Every page the product and the skeleton serve runs htmx 4.0.0, with no 2.x file referenced or
  loaded.
- Every behaviour a user can observe today is preserved, except where a change is named in this
  spec and deliberately chosen.
- The repo ends on htmx 4's own model, with no backwards-compatibility flag standing in for work
  not done.
- Reverting to 2.0.10 stays cheap for as long as 4.0 is young.
- The upgrade checklist already written into the architecture doc is closed out, item by item.

## Non-goals

- **Reworking the enhancement layer's design.** Its three responsibilities and its opt-in
  contract are unchanged. This is a translation plus one deliberate feature adoption, not a
  redesign.
- **`<hx-partial>`.** It replaces out-of-band swaps, and there are none in this repo outside the
  vendored library itself. Nothing to convert.
- **The reactive scripting extension.** The stack is fixed at htmx plus Alpine; a third scripting
  layer is a separate decision with its own justification to make.
- **The history-cache extension.** It restores the page cache 4.0 removed, which would reverse a
  change the security baseline benefits from.
- **The preload extension.** Nothing has asked for it.
- **Consuming htmx as a package dependency.** It stays vendored, as the stack rules require.
- **The epass.io cutover**, and anything gated on it.

## User stories

- As a user of the product without JavaScript, I want every form and link to keep working exactly
  as it does today, so that the upgrade is invisible to me.
- As a keyboard or screen-reader user, I want focus to still land somewhere sensible after a swap
  and controls to still announce themselves as busy mid-request, so that the upgrade does not cost
  me the accessibility work of spec 005.
- As a user filling in a form that re-renders with a validation error, I want anything I typed
  into a credential field cleared rather than retained, so that a failed attempt leaves nothing
  behind in the page.
- As someone starting a project from the skeleton, I want the template to give me the current
  htmx and correct guidance, so that I do not begin on a superseded major or on advice that no
  longer describes the library.
- As the operator, I want a bad 4.0 to be revertible in one step, so that adopting a day-old
  major is not a one-way door.

## Requirements

- **R1** The product and the skeleton both load htmx 4.0.0, obtained from the project's own
  release distribution, and no htmx 2.x file remains reachable by any served page.
- **R2** The enhancement layer's attribute-derivation behaviour is unchanged: opt-in only, an
  authored attribute always wins, transport derived from a form's method and action or an
  anchor's target, source attributes never altered, and re-running over the same element changes
  nothing.
- **R3** The enhancement layer's busy-state behaviour is unchanged: the control that submitted a
  request is disabled, marked busy to assistive technology and given the busy class for the
  duration, and restored on every completion path including error, timeout, abort, and a request
  cancelled by a later listener.
- **R4** The enhancement layer's focus-recovery behaviour is unchanged: focus is recovered only
  when a swap is what removed the focused element, and focus that survived a swap is never
  stolen.
- **R5** Every request the enhancement layer issues still carries the antiforgery token, and the
  no-JavaScript submit path still carries its own.
- **R6** No served markup depends on an attribute being inherited implicitly. Where a parent
  genuinely configures its descendants, the inheritance is stated explicitly in the markup. No
  compatibility flag is set to restore the previous inheritance model.
- **R7** A response the server did not intend as swappable content does not get swapped into the
  page. The behaviour of error and guard responses is the same as it is today.
- **R8** Swaps that preserve in-place state are adopted only where preserved state is desirable.
  No form that carries a credential, and no form that re-renders itself in place on a validation
  error, preserves user input across a swap.
- **R9** Requests that could outlive the library's new default request timeout are identified, and
  either fall within it or are given an explicit allowance.
- **R10** Wherever the product and the skeleton ship the same asset today, they ship the same
  asset afterwards.
- **R11** Returning to htmx 2.0.10 requires changing one file's contents and nothing else. The
  2.0.10 file remains in the repository, not deleted, for as long as this is true.
- **R12** Every item on the architecture document's pre-written upgrade checklist is either
  completed and ticked, or declined in writing with the reason.
- **R13** Every document that describes htmx behaviour, event names, configuration or the
  enhancement layer's mechanics is accurate against 4.0. No document names an event, attribute or
  configuration key that 4.0 does not have.
- **R14** The existing unit, integration and end-to-end suites pass. Behaviours that only became
  reachable, or only became falsifiable, under 4.0 gain their own assertions.
- **R15** Any additional vendored library is accompanied by a stated reason for needing it. The
  compatibility extension for running htmx alongside Alpine is evaluated and its adoption decided
  either way in writing.
- **R16** Request throughput and process memory are re-measured after the upgrade and compared
  against the figures already recorded for 2.x, so a performance regression is caught rather than
  discovered later.
- **R17** No part of this work introduces a Node package manager, a lockfile or a package
  manifest, including the upstream project's own upgrade-checking tool.

## Acceptance criteria

- **R1** — Given a page served by either the product or the skeleton, when its scripts load, then
  the htmx it runs reports version 4.0.0, and a search of everything served finds no 2.x build.
- **R2** — Given an element opting in through the enhancement convention, when the page loads or a
  fragment is swapped in, then the derived attributes are the same set, with the same values, as
  before the upgrade; and given an element with an authored attribute, when normalization runs,
  then that attribute is untouched.
- **R3** — Given a submitted form, when the request is in flight, then the submitting control is
  disabled, busy-marked and busy-classed; and when the request finishes by any path, then all
  three are undone and focus returns to that control if it survived and focus had not moved on.
- **R4** — Given focus inside a region about to be swapped, when the swap removes the focused
  element, then focus lands on an authored autofocus target inside the new content or on the
  swapped region itself; and given focus that was never inside the swapped region, when the swap
  completes, then focus has not moved.
- **R5** — Given any request the enhancement layer issues, when the server validates it, then the
  antiforgery token is present and accepted; and given the same form submitted with JavaScript
  disabled, then its token is present and accepted.
- **R6** — Given the served markup as a whole, when audited for attributes whose effect today
  depends on being inherited by a descendant, then each one found is marked as inherited
  explicitly, and the configuration contains no flag restoring implicit inheritance.
- **R7** — Given a request that a guard rejects, when the response returns, then no part of it is
  swapped into the page and what the user sees matches the pre-upgrade behaviour.
- **R8** — Given a form carrying a credential, when a failed submit re-renders it, then the
  credential field is empty; and given a swap target chosen for in-place preservation, when it is
  swapped, then scroll position and uncommitted state survive.
- **R9** — Given every endpoint the browser can reach, when the slowest is exercised, then it
  either completes inside the default timeout or has an explicit allowance recorded.
- **R10** — Given each asset shipped in both trees, when the two copies are compared, then they
  are byte-identical wherever they were byte-identical before.
- **R11** — Given a decision to revert, when the previous library file is put back in place, then
  the application runs on 2.0.10 with no other change required, and that file is present in the
  repository.
- **R12** — Given the architecture document's upgrade checklist, when this work completes, then no
  item is left unmarked, and each declined item carries its reason.
- **R13** — Given every document mentioning htmx, when read against the 4.0 reference, then every
  event name, attribute and configuration key it names exists in 4.0 and means what the document
  says it means.
- **R14** — Given the full test suite, when run, then it passes with no failures; and given each
  new assertion added by this work, when the code under it is changed to be wrong, then that
  assertion fails.
- **R15** — Given the repository afterwards, when its vendored libraries are listed, then each has
  a recorded reason for being there, and the Alpine compatibility extension is either present with
  a reason or absent with a reason.
- **R16** — Given the benchmark previously run against 2.x, when re-run against 4.0, then
  throughput and memory are recorded alongside the earlier figures and any regression is stated.
- **R17** — Given the repository afterwards, when searched, then it contains no package manifest,
  no lockfile and no package-manager invocation that it did not contain before.

## Open questions

None. The three decisions this work turned on were taken before the spec was written: adopt 4.0
now rather than wait; mark inheritance explicitly rather than set the compatibility flag; and
adopt in-place-preserving swaps selectively, never on forms carrying credentials or re-rendering
on validation error.
