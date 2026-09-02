# 021 — Response formats and content negotiation
Status: draft
Created: 2026-09-02

## Problem
NativeWeb renders HTML well and nothing else on purpose. The architecture doc's "Any content
type" section says a product that wants JSON, a feed or a sitemap builds the document itself
and returns it on the raw result primitives, and that the framework's job is only to stay out
of the way. In practice that has meant: the sample publishes nothing but HTML, the one JSON in
the tree is a hand-written serializer context because a reflective one is a trim warning under
NativeAOT, every non-HTML response is chunked (measured on 2026-08-31: a page without a
declared length loses 35% of its throughput), and there is no way for one URL to serve a
browser and a script differently. A product wanting a feed or an export today writes escaping,
media types, length handling and Accept parsing by hand, and gets each subtly wrong in its own
way. The user's direction on 2026-09-02: NativeWeb should serve server-rendered HTML and also
plain text, JSON, XML and most web text document formats, as framework helpers with negotiation,
proved in the sample.

## Goals
- A product returns any of the supported formats from a handler with one call and gets the
  right media type, charset, declared length and escaping without writing any of it.
- One route can offer several representations of the same resource, chosen by an explicit
  extension or by the Accept header, with a stated precedence and a stated failure.
- Every helper is NativeAOT-clean under the repo's TreatWarningsAsErrors: zero trim or AOT
  warnings, no reflection-based serialization.
- Every format is exercised by the sample through a real URL and proved in all three test
  tiers, so nw-dev demonstrates it after the next deploy.
- The documentation that currently says "no negotiation layer" is replaced, with the reversal
  recorded as a decision, and the doc parity tests keep it true.

## Non-goals
- Request-side negotiation or body parsing: reading JSON or XML request bodies, API versioning
  by media type, or anything about what a client sends. This spec is about what the server
  returns.
- A REST or API framework: no envelope conventions (JSON:API, HAL, problem+json), no OpenAPI,
  no MVC-style output formatter pipeline.
- Binary formats: PDF, images, spreadsheets in native form, archives. CSV is the export format.
- Streaming of unbounded bodies. The helpers buffer and declare a length, as the HTML result
  does; a streamed result is separate work.
- The thorough sample public site (article and tag detail pages, contact form, roles, audit
  trail, custom error pages). Those are the next specs; this one gives them the formats to use.
- Format conversion of any kind: HTML to Markdown, Markdown to HTML, XML transformation.
- Locale negotiation, which exists and is unchanged; feeds and exports are not translated
  beyond what the caller passes in.

## User stories
- As a product developer, I want to return a JSON listing, a plain-text or Markdown document,
  a CSV export, an Atom or RSS feed, and a sitemap from a handler in one call each, so that I
  never hand-write media types, escaping or length handling again.
- As a product developer, I want one route to answer a browser with HTML and a script with JSON
  when it asks for either by extension or Accept header, so that I do not maintain two routes
  for one resource.
- As a product developer, I want the framework to refuse a representation it does not offer in
  a defined way, so that a mistyped extension or a strange Accept header does not leak the
  wrong content type.
- As an operator reading nw-dev, I want the sample to publish a feed, a sitemap, a JSON listing,
  a CSV export and a text document, so that I can see each format working against real data.
- As a reader of the docs, I want the architecture doc, the library README and the
  configuration reference to describe what the framework does today, so that I do not port a
  hand-rolled negotiator into my product the way epass.io ported five files it did not need.

## Requirements
- R1. **One call per format.** A handler can return each of these with a single framework call:
  plain text, Markdown, JSON, XML, Atom, RSS, CSV and a sitemap. The response carries the
  registered media type for the format, a UTF-8 charset where the type takes one, a declared
  content length, and a body the caller did not have to escape.
- R2. **NativeAOT-clean.** The helpers, the negotiation and the sample's use of them build and
  publish with zero trim or AOT warnings under the repo's TreatWarningsAsErrors. JSON is
  serialized only through a caller-supplied, source-generated contract; nothing in the
  framework serializes by reflection.
- R3. **Escaping is the framework's job.** Arbitrary text placed in a title, a link, a summary
  or a cell (including quotes, angle brackets, ampersands, commas, newlines and characters
  outside the basic plane) yields a document that a standard parser for that format accepts,
  and reads back as the same text. A CSV cell that begins with a character a spreadsheet
  treats as a formula is neutralised so it opens as text.
- R4. **Structured feeds and sitemaps.** The Atom, RSS and sitemap helpers take a structured
  document (a feed with id, title, link, updated time and entries; entries with id, title,
  link, updated time and optional summary and author; sitemap entries with location and
  optional last-modified, change frequency and priority) and produce output that validates
  against the respective format's schema. Every link in a feed or sitemap is absolute; the
  base is taken from the request, honouring the forwarded-header configuration the pipeline
  already applies, unless the caller supplies one.
- R5. **Negotiation on one route.** A route can declare several representations of a resource.
  The representation is chosen, in this order: an explicit extension on the path; otherwise the
  Accept header, by quality value and specificity; otherwise the route's default. An absent
  Accept header or a bare wildcard selects the default.
- R6. **Refusal is defined.** An extension the route does not offer answers 404. An Accept
  header with no acceptable representation and no wildcard answers 406 and names the offered
  media types in the body. Neither leaks a body of another type.
- R7. **Caches stay correct.** A response chosen by the Accept header carries a Vary header
  naming Accept. A response chosen by extension does not. The public page cache never serves
  one representation under another's URL or type, and its existing locale variance is
  unchanged.
- R8. **Existing headers apply.** Every representation passes through the same security
  headers, rate limiting and request logging as HTML. Non-HTML responses carry the nosniff
  header, depend on no nonce, and the content-security policy does not break them.
- R9. **The sample proves every format.** The sample publishes, against its real article data:
  a JSON listing of published articles; the same listing as CSV; the same listing negotiated
  from the existing HTML listing route by extension and by Accept; an Atom feed and an RSS
  feed of recent articles; a sitemap listing the public pages; a robots.txt that names the
  sitemap; and one plain-text or Markdown document. Each is fetched and checked in the
  integration tier, and the negotiated route and the feed are exercised through a browser in
  the e2e tier.
- R10. **The docs tell the truth.** The architecture doc's "Any content type" section is
  rewritten to describe the helpers and the negotiation rules, with a decision record for the
  reversal. The library README that owns the helpers documents each format and the precedence
  in R5. The docs site is rebuilt. The existing doc parity tests pass over the rewritten text,
  and any new configuration key is in the configuration reference.
- R11. **Three tiers.** Unit tests cover each helper's escaping, media type, length and the
  negotiation precedence and refusals, including the failure paths. Integration tests fetch
  each sample URL against a real database. End-to-end tests drive the negotiated route and the
  feed through a browser. Each new assertion is shown to fail when its code is broken.

## Acceptance criteria
- Given a handler that returns a JSON listing through the helper, when a client requests it,
  then the response is `application/json; charset=utf-8` with a Content-Length equal to the
  body and no `Transfer-Encoding: chunked` (R1, R2).
- Given `dotnet publish` of the sample in Release with AOT, when the build completes, then it
  reports zero warnings (R2).
- Given an article whose title is `<b>"Q&A" & Co,</b>` with a newline inside it, when it is
  emitted as XML, Atom, RSS, JSON and CSV, then each document parses with a standard parser and
  the title reads back unchanged, and the CSV cell for a title beginning with `=` opens as text
  (R3).
- Given a feed with two entries, when the output is checked against what RFC 4287 and RSS 2.0
  require (a feed id, title and updated time, an author on the feed or on every entry, an id,
  title and updated time on every entry; a channel title, link and description, a title and
  guid on every item), then it passes, the sitemap validates against the sitemaps.org schema,
  and every link starts with the scheme and host of the request, or with the supplied base when
  one is given (R4). Neither Atom nor RSS publishes an XML Schema, so the check is structural.
- Given a route offering HTML by default and JSON as an alternative, when a client requests the
  path with a `.json` extension and `Accept: text/html`, then it receives JSON; when it sends
  `Accept: application/json` with no extension, it receives JSON with `Vary: Accept`; when it
  sends no Accept header, it receives HTML; when it sends
  `Accept: text/html;q=0.5, application/json;q=0.9`, it receives JSON (R5, R7).
- Given the same route, when a client requests a `.xml` extension the route does not offer,
  then it receives 404; when it sends `Accept: image/png` alone, it receives 406 naming
  `text/html` and `application/json` (R6).
- Given a cached route offering two cookie-free representations, when one client fetches the
  first and another the second of the same path within the cache window, then each receives its
  own type, and the locale variance still holds. In the sample the feed's Atom and RSS
  representations are stored and the article listing is not cached in any representation: its
  filter form embeds an antiforgery token, so a returning visitor's render (which sets no cookie)
  would be stored and served to a stranger whose cookie cannot match it, measured on 2026-09-02;
  a public page is one that carries no token. The HTML is still never served to a JSON request
  (R7).
- Given any non-HTML sample response, when its headers are read, then it carries
  `X-Content-Type-Options: nosniff` and the same rate-limit policy as the HTML pages (R8).
- Given the deployed sample, when `/sitemap.xml`, `/robots.txt`, the feed URLs, the JSON and
  CSV listings and the text document are fetched, then each answers 200 with its media type,
  and robots.txt names the sitemap URL (R9).
- Given the rewritten architecture section, when the repo tests run, then the doc claim tests
  pass and a decision file dated today records the reversal (R10).
- Given each new test, when the helper or negotiation it covers is broken, then that test goes
  red (R11).

## Open questions
None. Inferences made rather than asked, each reversible in `/plan`: extension beats Accept
because it is the more explicit ask; a bare wildcard selects the default because browsers send
one; Markdown is included because it costs one media type; Atom and RSS are both offered
because readers still differ; streaming is deferred because the measured cost of chunking makes
buffered-with-length the right default for every document a page-sized helper produces.
