# 017 — Scaffold list sorting and cursor pagination

Status: draft
Created: 2026-09-01

## Problem

A NativeWeb scaffolded list cannot be sorted, and it pages by offset. The persistence seam every
generated CRUD endpoint calls offers a free-text search and a row window and nothing else, so the
order rows arrive in is whatever the store happened to return, and page seven is fetched by
skipping a hundred and twenty rows. Neither is a gap a consumer can close from outside: the
ordering has to reach the store, and the store is behind an interface that has no parameter for
it.

Consumers have paid for this three times. `ghosthive.com/apps/control-plane` wrote a
`ListQueryParser`, a `FilterComposer` and a keyset `Cursor` of its own, and then did not use them
on its own lists — the framework's seam could not carry what it had built, so the work sat unused
beside a list that still could not be sorted. That is the shape of the whole consumer-readiness
finding: a product reimplements a framework part, badly or not at all, because the framework's
seam has no room for it. Offset paging is also wrong on its own terms once rows are being
inserted, which for an admin list is always: a row added while an operator reads page two pushes
a row from page two onto page three, and the operator never sees it.

## Goals

- A scaffolded list can be ordered by any property the model marks sortable, in either direction,
  from the generated UI and from the query string.
- Paging is keyset, so a page is defined by where the last one ended rather than by how many rows
  precede it — no row is skipped or repeated when the underlying table changes between requests.
- The set of sortable properties is declared on the model and enforced by the framework, so a
  client cannot name a property the model did not offer.
- A consumer implementing the store seam is told what changed and how to migrate, in a document
  this repository keeps from now on.

## Non-goals

- **No structured filtering and no query language.** No per-field predicates, no operators, no
  expression parsing. `[ScaffoldSearch]` free-text search is the whole of filtering and stays
  exactly as it is. NativeWeb has no ORM, JauntyQ owns data access, and the seam's stated design
  is a contract rather than a way of querying.
- **No numbered pages.** A cursor knows next and previous; it does not know page seven. Jump-to-page
  is not being preserved in another form.
- No multi-column sort. One sort property at a time, plus the framework's own tiebreaker.
- No change to create, read, update or delete.
- No migration of any consumer product. Those are their own repositories' work.
- No sort or paging state persisted per user.

## User stories

- As an operator reading a scaffolded admin list, I want to order it by any column the model
  offers, so that I can find the newest, the largest or the alphabetically first row without
  paging through everything.
- As an operator paging through a list that other people are writing to, I want each page to
  continue where the last ended, so that a row inserted while I read is not a row I never see.
- As a product developer implementing the store seam, I want the framework to hand me the
  sortable-property allow-list the same way it hands me the searchable one, so that I do not
  re-declare it by hand and cannot forget to reject an unknown property name.
- As a product developer upgrading between NativeWeb versions, I want a document that names every
  breaking change and how to migrate, so that a removed or altered seam is something I read about
  rather than something my build discovers.

## Requirements

- **R1** A model may mark any property as sortable. Marking is per-property, declarative, and
  mirrors how a property is marked searchable today.
- **R2** The generated list orders rows by a sortable property named in the request, in ascending
  or descending direction.
- **R3** When the request names no sort, the list uses a defined default order, and that order is
  the same on every request for the same data.
- **R4** Every order the framework requests is a total order: rows are never ambiguously ranked,
  even when the sorted property holds duplicate values across rows.
- **R5** A sort property the model did not mark sortable is rejected. The list still renders,
  using the default order, and the unknown name never reaches the store.
- **R6** The persistence seam receives the ordering and the sortable-property allow-list as
  parameters. An implementation does not re-declare which properties are sortable.
- **R7** Paging is by cursor. A page request carries a position produced by a previous page, not
  an offset.
- **R8** A cursor is opaque to the client: it carries no meaning a client is invited to read,
  construct or edit.
- **R9** A cursor that is absent, malformed, or does not match the current sort does not error and
  does not reach the store as-is. The list renders from the beginning of the current order.
- **R10** The list can move forward and backward through the order by cursor, and reports when
  there is nothing further in either direction.
- **R11** The list reports the total number of matching rows.
- **R12** Sort and position survive a search: changing the page does not clear the search term, and
  the three are expressible together in one shareable URL.
- **R13** The generated list UI offers ordering on exactly the sortable properties, indicates which
  property is currently ordering the list and in which direction, and offers reversing it.
- **R14** The change to the persistence seam is breaking, and is recorded in a breaking-change
  document this repository keeps, with what changed and how to migrate. That document covers the
  seams removed before this spec as well.
- **R15** Everything shipped is NativeAOT-clean with no new trim or AOT warnings, under the
  repository's existing warnings-as-errors setting.
- **R16** Covered at all three tiers: unit, integration against a real PostgreSQL instance, and
  end-to-end through a browser.

## Acceptance criteria

- **R1/R6** — Given a model marking two of its five properties sortable, when the list is
  generated, then the store receives those two property names as the sortable set and the store
  itself declares nothing.
- **R2** — Given a list of rows with distinct values for a sortable property, when the request asks
  for that property descending, then the rows arrive in descending order of it.
- **R3** — Given a request naming no sort, when the list renders twice against unchanged data,
  then both renders are in the same order.
- **R4** — Given twenty rows sharing one value in the sorted property, when the list is paged all
  the way through, then every row appears exactly once and none is skipped.
- **R5** — Given a request naming a property the model did not mark sortable, when the list
  renders, then it is in the default order and the store was never asked for the named property.
- **R7/R10** — Given more rows than fit one page, when the operator follows next and then previous,
  then they are returned to the page they started on, with the same rows.
- **R8/R9** — Given a cursor edited by hand into something unreadable, when the list renders, then
  it shows the first page of the current order and does not error.
- **R4/R7** — Given an operator on the second page, when a row is inserted that would sort onto the
  first page, then the operator's next page contains no row they have already seen and omits no
  row they have not.
- **R11** — Given a search matching a known number of rows, when the list renders, then it reports
  that number.
- **R12** — Given a search term, a sort and a position, when the resulting URL is opened in a fresh
  session, then the same rows render in the same order.
- **R13** — Given a model with sortable properties, when the list renders, then only those
  properties offer ordering, and the one currently ordering the list is distinguishable from the
  rest along with its direction.
- **R14** — Given a developer holding a store implementation written before this change, when they
  read the breaking-change document, then it names the seam that changed and what their
  implementation must now accept.
- **R16** — Given the full suite, when it runs, then sorting, cursor round-tripping, allow-list
  rejection and the browser-level ordering and paging are each asserted, and each assertion fails
  when the behaviour it covers is broken.

## Open questions

None. Four decisions were taken before this spec was written and are recorded here as settled
scope rather than as questions:

- Filtering stays free-text; no query language. (Non-goals.)
- The seam breaks rather than being shimmed, and the breaking-change document is written as part
  of this work. (R14.)
- Sortable properties are declared by attribute, mirroring the searchable set. (R1.)
- The numbered pager becomes next/previous with a total count retained. (R10, R11.)
