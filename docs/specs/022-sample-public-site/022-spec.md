# 022 — Sample public site
Status: done
Created: 2026-09-02

## Problem
The sample's public face is one listing page. `/` redirects to the admin, an article has no page
of its own, and a tag is a row in an admin table that nothing on the public side reads. The user
looked at nw-dev on 2026-09-02 and said the examples need to be more thorough and practical: a
product built on NativeWeb is mostly hand-written public pages, and the reference application
shows one of them. Spec 021 gave the sample a feed, a sitemap, a JSON listing and a text
document, and every one of them links to a listing rather than to the article it names, because
there is nothing to link to. The two things the next specs need, an article with a stable public
address and a tag that relates to articles, do not exist: articles have an 8-character random key
and no slug, tags live only in memory even on the database path, and no relation joins them.

## Goals
- A visitor arriving at the site's root sees a home page, can open any published article at a
  readable, stable address, and can browse by tag from any page.
- Every document the sample already publishes (listing, feed, sitemap, about) links to the
  article and tag pages, so a feed reader or a crawler lands on the right page.
- Tags and the article-tag relation exist on both the in-memory and the database paths, seeded
  on both, so the next spec (the admin side of the relation) edits something real.
- The pages demonstrate the two cache shapes the framework offers: a cookie-free page served
  from the shared cache, and a page that is rendered per visit because each visit changes state.
- Every page is proved in all three test tiers and is visible on nw-dev after the next deploy.

## Non-goals
- Admin editing of the article-tag relation, dropdowns, multi-selects, dates, enums and status
  fields. That is the next spec (part (b) of the user's list); this one gives it the data.
- Rendering the article body as Markdown or any other markup. The body is plain text, shown as
  paragraphs, as the admin typed it. Format conversion was a 021 non-goal and stays one.
- Redirecting an old slug after an admin edits it. An edited slug is a new address; the old one
  answers 404. Documented, not prevented.
- A custom 404 or 500 page. An unknown article or tag answers the application's current 404;
  the custom pages are part (d).
- Pagination of the home, tag or listing pages past their fixed window. Each shows its newest
  twenty; the listing's existing filter form is unchanged.
- Comments, related articles, per-article feeds, search across pages, view-count analytics
  beyond the one integer the admin already lists.
- Any change to the framework libraries beyond one seam: a store may refuse a save with a
  message the scaffolded form shows. Found at `/plan`: the generated create and update handlers
  had no way to say no, and R2's refusal cannot be met without it. Everything else here is
  sample and schema; any other framework gap found on the way is a note, not a fix.

## User stories
- As a visitor, I want the site's root to be a page rather than a redirect to an admin sign-in,
  so that I can see what the site publishes.
- As a visitor, I want each article at an address made of words, so that I can read a link
  before I follow it and share one that still works after the title is edited.
- As a visitor, I want to see an article's tags and follow one to everything else with that tag,
  so that I can browse by subject.
- As a feed subscriber or crawler, I want feed entries and sitemap locations to point at the
  article itself, so that I land on the page and not on a listing.
- As an admin, I want a slug filled in from the title when I create an article and a clear
  refusal when I choose one already in use, so that two articles never share an address.
- As a product developer reading the sample, I want to see a public read that writes (the view
  count) next to one that does not (the tag page), so that I can see why one is cached and the
  other is not.
- As the author of the next spec, I want tags and the article-tag relation persisted and seeded
  on the database path, so that the admin editing I add works against real rows.

## Requirements
- R1. **A home page at the root.** The root of the site answers a page, not a redirect. It shows
  the newest published articles (title, author where present, date, tags, each linking to the
  article), the list of tags, and links to the listing, the feed, the about document and the
  admin. It is the same page for every visitor.
- R2. **Every article has a slug.** A slug is unique across articles, made of lowercase letters,
  digits and hyphens, and is the article's public address. When an admin creates an article
  without one it is derived from the title; when the title is later edited the slug does not
  change. An admin may edit the slug; a value already in use, or one that is not made of the
  permitted characters, is refused with a message on the form, and nothing is saved. Every
  article that exists before this change receives a slug derived from its title, made unique
  where titles collide.
- R3. **An article page.** A published article is served at an address made of its slug. The
  page shows the title, the author where present, the creation date, the body as paragraphs,
  and the article's tags, each linking to that tag's page. An article that does not exist and
  an article that exists but is not published answer the same 404: nothing about an unpublished
  article is visible to the public, including whether it exists.
- R4. **Tags and the relation persist.** Tags and the article-tag relation exist on both the
  in-memory and the database paths. An article carries zero or more tags; a tag may have no
  articles. The in-memory path is seeded so that at least one article carries two tags, at least
  one carries none, and at least one tag has no published article. The database path seeds the
  same tags and no articles, as it seeds none today; the tests create the relations they assert
  on (amended at `/plan`). The existing tag admin continues to work on both paths.
- R5. **Tag pages.** A tag listing shows every tag with a count of its published articles, each
  linking to the tag's page. A tag page shows the tag's name and its published articles, newest
  first, in the same shape as the home page's list, and says so when there are none. An unknown
  tag answers 404. Unpublished articles are never counted and never listed.
- R6. **A view is counted.** Each successful render of an article page adds one to that
  article's view count, the count the admin already sees. Two visits arriving at the same
  moment are both counted. A request answered 404 counts nothing. The count is not shown on the
  public page.
- R7. **Caches stay correct.** The home page, the tag listing and every tag page carry no
  form, no cookie and no per-visit value, and are served as cacheable public pages in the same
  way the feed is. The article page is rendered on every visit and is never stored in or served
  from a shared cache, because each visit changes the count. The listing page's behaviour from
  021 is unchanged.
- R8. **Everything already published links here.** Each item on the listing page links to its
  article. Each feed entry's link is the article's page. The sitemap lists the home page, the
  tag listing, every tag page and every published article page, with each article's last-
  modified time. The about document names the new addresses. The JSON and CSV listings gain the
  slug and the tags of each article. Nothing links to an unpublished article.
- R9. **Existing protections apply.** Every new page passes through the same security headers,
  rate limiting and request logging as the pages that exist; every value shown is encoded; a
  slug in the address that is not a valid slug answers 404 without reaching the data.
- R10. **The docs tell the truth.** Wherever the sample's routes are described (the sample's
  own README, the about document, the architecture or quick-start pages that walk the sample),
  the new pages appear, and the doc parity tests that hold those documents to the tree pass.
- R11. **Three tiers.** Unit tests cover slug derivation and refusal, the view increment under
  concurrent visits, and each page's rendering including its empty states. Integration tests
  fetch every new page against a real database on both paths and prove the 404s, the cache
  behaviour of R7 and the links of R8. End-to-end tests drive home to article to tag and back
  through a browser against the published binary. Each new assertion is shown to fail when its
  code is broken.

## Acceptance criteria
- Given a visitor with no cookies, when they request the root, then they receive a 200 HTML page
  listing the seeded published articles with their tags, and following any article link or tag
  link lands on a 200 page for that article or tag (R1, R3, R5).
- Given an admin creating an article titled `Hello, World! (Again)` with the slug left blank,
  when the form is submitted, then the saved slug is `hello-world-again`; when a second article
  with the same title is created, its slug differs and both remain reachable; when the first
  article's title is then edited, its slug is unchanged (R2).
- Given an admin editing an article's slug to one another article holds, or to `Not A Slug`,
  when the form is submitted, then the form is shown again with a message naming the problem
  and neither article changed (R2).
- Given a database created before this change with three articles, two of them titled alike,
  when the migration runs, then every article has a slug, no two are equal, and each is derived
  from its title (R2).
- Given an article that is not published, when its slug is requested, then the response is the
  same 404, in status and headers, as a slug that belongs to no article (R3).
- Given the seeded data on either path, when the tag listing is requested, then every seeded tag
  appears with the number of its published articles, a tag with none shows zero and its page
  says there are no articles, and an unknown tag answers 404 (R4, R5).
- Given an article with a view count of zero, when its page is requested fifty times
  concurrently, then the admin shows a count of fifty; when a missing slug is requested, no
  count anywhere changes (R6).
- Given two cookie-free requests for the home page within the cache window, when the second is
  served, then it comes from the cache and carries the same public cache header as the feed;
  given two requests for one article page, when both are served, then each is a fresh render
  and neither response is marked cacheable (R7).
- Given the feed, the sitemap, the JSON listing and the listing page, when each is fetched, then
  every article reference in each is that article's page address, the sitemap includes the home
  page, the tag listing and every seeded tag page, and no unpublished article appears in any of
  them (R8).
- Given a request for a slug containing a space, a dot or an upper-case letter, when it is
  handled, then the answer is 404 and no query reached the store (R9).
- Given the rewritten sample documentation, when the repo tests run, then every path and address
  it names resolves and the doc parity tests pass (R10).
- Given each new test, when the code it covers is broken, then that test goes red (R11).

## Open questions
None. Inferences made rather than asked, each reversible in `/plan`: the slug alphabet is
lowercase ASCII letters, digits and single hyphens, because that is what every address bar and
feed reader treats as plain; a derived slug is made unique by a numeric suffix; the home page and
tag pages show the same twenty-article window the listing and feed do; the view count is added
by the store in one step rather than read and written back by the page, because the
concurrent-visit criterion cannot be met any other way; the last-modified time in the sitemap is
the article's creation time, since the sample records no edit time.
