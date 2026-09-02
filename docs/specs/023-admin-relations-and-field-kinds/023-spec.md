# 023 — Admin relations and field kinds
Status: done
Created: 2026-09-03

## Problem
The sample admin can edit strings, numbers, a checkbox and a timestamp, and nothing else. The
article-tag relation that spec 022 persisted is read-only from the admin: a tag reaches an
article only through a seed or a test helper. A product built on the scaffold meets a status,
a date, a pick-one and a pick-many field in its first week, and today each of those is a field
the generator silently drops from the form. The user also needs to see a checkbox list and a
multi-select side by side, on real data, to decide which one products should reach for.

## Goals
- An admin can set an article's tags, status, category and publication date from the article
  form, and the public site reflects each change on the next request.
- The scaffold renders four new field kinds from the model alone: a fixed choice (enum), a
  date without a time, a pick-one over another entity, and a pick-many over another entity,
  the last in either of two controls.
- The two pick-many controls edit the same relation from opposite sides, so the user can
  compare them on identical data.
- Everything holds on both store paths and is proved in all three tiers.

## Non-goals
- No new public pages. Categories get no public listing; the public site changes only where
  status and publication date replace what it read before.
- No ordering of tags within an article, no tag colours in the form, no tag creation from the
  article form.
- No localisation of enum labels; the member name, spaced, is the label.
- No date-and-time picker beyond the one that exists; no time zones in the date field.
- No search or sort on the new relation columns.

## User stories
- As an admin, I want to tick the tags an article carries, so that the tag pages list it.
- As an admin, I want to pick the articles a tag applies to from the tag's own form, so that
  retagging many articles is one save.
- As an admin, I want a status of Draft, Published or Archived instead of a checkbox, so that
  an article can leave the site without pretending it was never published.
- As an admin, I want to set the date an article counts as published, so that the home page
  orders by editorial date rather than the moment the row was created.
- As an admin, I want to file an article under one category, so that a product has a
  pick-one example to copy.
- As a framework consumer, I want an enum, a date, a reference and a collection property to
  render as the right control with no hand-written form, so that the scaffold covers a real
  entity.
- As the user evaluating the framework, I want the checkbox list and the multi-select on the
  same relation, so that I can judge them on feel rather than on a description.

## Requirements
- R1 The article form offers every tag as a checkbox list. Saving sets the article's tags to
  the ticked set, adding and removing links as needed, on both store paths.
- R2 The tag form offers every article as a multi-select. Saving sets the tag's articles to the
  selected set. R1 and R2 edit one relation: a link made on either side shows on the other.
- R3 The article's published checkbox is replaced by a status with the values Draft, Published
  and Archived, rendered as a dropdown. Existing rows convert: published becomes Published,
  unpublished becomes Draft. The public site, feed, sitemap, JSON and CSV treat only Published
  as visible; Archived is as invisible as Draft, including the article page.
- R4 The article gains an optional publication date, a calendar date with no time, rendered
  as a date picker and shown in the admin list. Public ordering is by publication date, newest
  first, with creation time as the tiebreak and as the fallback for a row without one.
- R5 A category entity exists in the admin with a name and a key, with list, create, edit and
  delete. An article belongs to at most one category, chosen from a dropdown that includes a
  "none" choice. Deleting a category leaves its articles with no category. The category shows
  in the article list.
- R6 The scaffold derives each new control from the model alone: an enum property renders a
  dropdown of its members; a date-only property renders a date picker; a property referring
  to another scaffolded entity renders a dropdown of that entity's rows; a collection of
  another entity's keys renders a checkbox list, or a multi-select where the model says so.
  A model needs no hand-written form and no sample-specific code in the framework.
- R7 A posted value outside the offered set (an unknown enum member, a tag or category key
  that does not exist, a malformed date) is refused: the form re-renders with the message
  beside the field and nothing is saved. Unticking every checkbox or selecting nothing is a
  valid empty set, not an error.
- R8 The view count is no longer posted back from the article form, so a visit counted while
  the form is open is kept. The field stays visible in the form and the list.
- R9 Deleting a tag removes its links on both store paths; re-creating a tag with the same key
  starts with no articles. A tag's key typed on the create form is kept on both paths.
- R10 Every new control and rule is proved in the scaffold's own tiers against a test model
  that is not the sample, and in the sample's tiers through the real forms on both stores.
- R11 The reference docs describe the four field kinds, when to choose the checkbox list over
  the multi-select, and how a consumer declares each.

## Acceptance criteria
- Given a Published article with no tags, when the admin ticks NativeAOT and HTMX and saves,
  then `/tags/aot` and `/tags/htmx` list the article and its page shows both tags.
- Given that article, when the admin opens the HTMX tag's form, then the article is selected in
  the multi-select; when the admin deselects it and saves, then the article's form shows HTMX
  unticked and `/tags/htmx` no longer lists it.
- Given a row that was published before the change, when the migration runs, then its status
  is Published and it is still on the home page; an unpublished row is Draft.
- Given a Published article, when the admin sets Archived and saves, then its page answers 404
  with the same body as an unknown slug, and it leaves the home page, feed, sitemap, JSON and
  CSV.
- Given two Published articles, when the older one gets a later publication date, then it
  leads the home page.
- Given a category "Guides", when an article picks it and saves, then the list shows Guides in
  its row; when the category is deleted, then the article's row shows no category and its form
  shows "none" selected.
- Given a posted status of "Deleted", or a tag key that does not exist, or a date of
  "2026-02-30", when the form is submitted, then the response is the form with a message under
  the field, and the stored row is unchanged.
- Given an article at 40 views, when the admin opens its form, waits for 3 visits and saves,
  then the count is 43.
- Given the test model in the scaffold's tiers, when it declares an enum, a date-only, a
  reference and a collection property, then the generated form carries a select, a date input,
  a select with a blank option, and a checkbox list or multi-select as declared, and posting
  each valid and each invalid value behaves as R7 says.

## Open questions
- None.
