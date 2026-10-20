# 021 — Plan
Spec: ./021-spec.md

## Approach

Two small additions to two existing libraries, no new library. `libs/nativeweb.render` already
owns "what the body is" (`TemplateResults.Html` renders into a private buffer, declares the
length and writes once, and the 2026-08-31 measurement in that file is why). It gains
`FormatResults`, one static method per format, every one of them a buffered UTF-8 body with a
declared length and a fixed media type, plus a `Format` value (extension, media type) that the
helpers and the negotiator share. `libs/nativeweb.web` already owns the pipeline and the
endpoints, so it gains `MapNegotiated`: one call maps `pattern` and `pattern.{format}`, chooses
the representation **before the handler runs** from the offers declared at map time, and hands
the handler the chosen `Format`. Refusals (404 for an extension not offered, 406 for an Accept
header nothing matches) therefore happen without a database round-trip, the `Vary` header is
set by the framework rather than remembered by each handler, and the output cache can key on
the same pure choice instead of on the raw `Accept` string.

Rejected: (a) a handler returning a deferred set of representation thunks. It reads well but the
offers are only known after the handler has fetched its data, so a mistyped extension costs a
query before it can 404, and the cache policy cannot know the variant at lookup time. (b)
Varying the output cache by the `Accept` header. Every distinct spelling of the header would
mint a cache entry, the exact fragmentation the locale key was designed to avoid. (c) MVC
output formatters or a `Results.Json` with reflection. Both are trim warnings under NativeAOT and
the repo builds with `TreatWarningsAsErrors`. (d) A new `libs/nativeweb.formats`. It would add a
project, a README and a test tier for eight methods that belong beside `TemplateResults`.

## Tech / dependencies

- .NET 10.0.203 SDK on this machine, C# 14, NativeAOT, `TreatWarningsAsErrors` repo-wide.
- `System.Text.Json` source generation (`JsonTypeInfo<T>`, `JsonSerializerContext`), in the
  shared framework. The sample adds a `JsonSerializerContext` for its listing type.
- `System.Xml.XmlWriter` for XML, Atom, RSS and the sitemap. In the shared framework, AOT-safe,
  and `System.Private.Xml` is already in the sample's publish output.
- `Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParseList` for the Accept header, in the
  shared framework.
- **No new NuGet package.** `System.ServiceModel.Syndication` was considered for reading feeds
  back in tests and is not in the local cache; the tests validate with `XDocument` against the
  RFC 4287 and RSS 2.0 required elements, and the sitemap against the sitemaps.org XSD vendored
  into the test project (`XmlSchemaSet`, in the shared framework).

## Changes by area

- `libs/nativeweb.render/Format.cs` (new) — `Format` readonly record struct (`Extension`,
  `MediaType`, `Charset` flag) with statics `Html`, `Text`, `Markdown`, `Json`, `Xml`, `Atom`,
  `Rss`, `Csv`; `ContentType` composes the header value; `TryFromExtension` is the one table
  from `json` to `application/json`. R1, R5.
- `libs/nativeweb.render/FormatResults.cs` (new) — `Text(string)`, `Markdown(string)`,
  `Json<T>(T, JsonTypeInfo<T>)`, `Xml(Action<XmlWriter>)`, `Atom(Feed, Uri)`, `Rss(Feed, Uri)`,
  `Csv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)`,
  `Sitemap(IEnumerable<SitemapEntry>, Uri)`. Each returns a private `BufferedResult` (bytes,
  content type, status) that sets `ContentLength` and writes once, mirroring
  `TemplateHtmlResult`. R1, R2, R3.
- `libs/nativeweb.render/Feed.cs` (new) — `Feed(Id, Title, Link, Updated, Entries, Author?,
  Subtitle?)`, `FeedEntry(Id, Title, Link, Updated, Summary?, Author?)`, `SitemapEntry(Location,
  LastModified?, ChangeFrequency?, Priority?)`. Links are `string` paths or absolute URIs,
  resolved against the base with `new Uri(base, link)`. R4.
- `libs/nativeweb.render/Csv.cs` (new, internal) — RFC 4180 writer: CRLF rows, quote a field
  holding a comma, quote, CR or LF, double the quotes, UTF-8 without BOM. Formula neutralising: a
  field beginning with `=`, `+`, `-`, `@`, tab or CR is prefixed with a single quote and quoted,
  unless it parses as an invariant decimal number. The effective first character is found after
  skipping any leading whitespace, control or format characters (Unicode Zs, Cc, Cf), because
  spreadsheet importers trim before they sniff and a zero-width or bidi character hides the
  `=`; LF is in the list beside CR. R3.
- `libs/nativeweb.render/RequestBase.cs` (new) — `HttpRequest.PublicBase()` extension: `Uri`
  from `Scheme` and `Host`, which `UseForwardedHeaders` has already rewritten by the time any
  endpoint runs (`NativeWebPipeline.cs:468`). `Atom`, `Rss` and `Sitemap` each get an overload
  taking `HttpRequest` in place of the `Uri`, so the request-derived base is the framework's and
  the `Uri` overload is the caller-supplied one the spec allows. R4.
- `libs/nativeweb.web/Negotiation.cs` (new) — `Negotiation.Choose(HttpRequest, offers, out
  Format chosen, out bool byAccept)` pure function: extension route value first, then Accept
  ordered by `q` descending and specificity (`type/sub` over `type/*` over `*/*`), then
  `offers[0]`; returns a `Refusal` of `NotFound` or `NotAcceptable`. `NegotiationMetadata`
  endpoint metadata carrying the offers. R5, R6.
- `libs/nativeweb.web/NegotiatedEndpoints.cs` (new) — `MapNegotiated(this
  IEndpointRouteBuilder, string pattern, Format[] offers, Func<HttpContext, Format,
  ValueTask<IResult>> handler)` maps `pattern` and `pattern + ".{format}"` with plain
  `HttpContext` lambdas (so the Request Delegate Generator binds nothing it cannot see), attaches
  `NegotiationMetadata`, runs `Choose`, answers 404 or 406 (text/plain body naming the offered
  media types), sets `Vary: Accept` when the Accept header decided, stores the chosen `Format`
  in `HttpContext.Items`, then calls the handler. Returns the two `RouteHandlerBuilder`s as one
  `NegotiatedRouteBuilder` so `.CacheOutput(...)` and `.RequireRateLimiting(...)` apply to both.
  R5, R6, R7, R8.
- `libs/nativeweb.web/PublicPageCachePolicy.cs` — when the endpoint carries
  `NegotiationMetadata`, run `Choose` in `CacheRequestAsync` and add `VaryByValues["format"]`;
  in `ServeResponseAsync` refuse to store a response whose `Content-Type` is not the chosen
  format's media type, so a handler that answers another type can never be stored under the
  chooser's key. No `?format=` query escape hatch: the chooser reads only the route value and
  the Accept header, and the `page` query allowlist stays as it is. Everything else unchanged.
  R7.
- `libs/nativeweb.web/NativeWebPipeline.cs` — no order change. The negotiated endpoints sit
  behind the same security headers, rate limiter, output cache and request logging. R8.
- `apps/sample/SampleJson.cs` (new) — `JsonSerializerContext` for
  `IReadOnlyList<PublishedArticle>`. R2, R9.
- `apps/sample/PublicArticleEndpoints.cs` — `GET /articles` becomes `MapNegotiated("/articles",
  [Html, Json, Csv], ...)` with `.CacheOutput(NativeWebPipeline.PublicPagePolicy)`; the HTML
  branch is the existing `Page`, JSON is the listing through `SampleJson`, CSV is
  id/title/author/created. The `MapFormPost` is unchanged. R5, R7, R9.
- `apps/sample/PublicDocuments.cs` (new) — `MapNegotiated("/feed", [Atom, Rss], ...)` over the
  twenty most recent published articles; `GET /sitemap.xml` listing `/articles`, `/feed.atom`
  and `/about`; `GET /robots.txt` (`User-agent: *`, `Disallow: /admin`, `Disallow: /version`,
  `Sitemap: <base>/sitemap.xml`); `MapNegotiated("/about", [Markdown, Text], ...)` serving a
  short document about the sample. All with the public-page cache policy and the general
  rate-limit policy. R9.
- `apps/sample/Program.cs` — one `app.MapPublicDocuments()` beside `MapPublicArticles`. R9.
- `docs/01-architecture/progressive-enhancement.md` — the "Any content type (R12)" section
  rewritten: the helpers, the precedence, the refusals, the cache rule, and what stays on the
  raw primitives (bytes, streams). R10.
- `docs/decisions/2026-09-02-009-response-formats-in-the-framework.md` (new) — why the
  no-negotiation-layer stance is reversed, what it cost, and the line that still holds (no
  request-side formats, no API framework). R10.
- `libs/nativeweb.render/README.md` — "The whole API" table gains the eight helpers and the
  `Format` value; a section on escaping and lengths. `libs/nativeweb.web/README.md` — a
  "Negotiated endpoints" section with the precedence and refusals. R10.
- `docs-site/` — regenerated by `bash scripts/build-docs.sh`; `deno task -c
  tools/docs-site/deno.json check-site` green. R10.
- `docs/02-reference/breaking-changes.md` — no entry: everything is additive. `MapGet
  "/articles"` in the sample changes shape, but the sample is not a consumer seam.
- `work/status/current.md`, `work/status/previous.md`, `CHANGELOG.md` — per the implement flow.

### Tests (R11)

- `tests/nativeweb.render.tests.unit/FormatResultsTests.cs` — per helper: media type, charset,
  `Content-Length` equals body, no chunking (run through `DefaultHttpContext` with a
  `MemoryStream` body); the spec's title `<b>"Q&A" & Co,</b>` with an embedded newline and a
  supplementary-plane character round-trips through XML, Atom, RSS, JSON and CSV parsers.
- `tests/nativeweb.render.tests.unit/CsvTests.cs` — RFC 4180 cases, the formula prefix, the
  negative-number exemption, CRLF, BOM absence.
- `tests/nativeweb.render.tests.unit/FeedTests.cs` — Atom: namespace, `feed/id`, `title`,
  `updated` RFC 3339, each entry's required elements, links absolute against the base and the
  supplied base; RSS: `rss@version=2.0`, `channel/title|link|description`, `pubDate` RFC 822,
  `guid`; sitemap validates against the vendored `sitemap.xsd`.
- `tests/nativeweb.web.tests.unit/NegotiationTests.cs` — the precedence table from the spec's
  acceptance criteria, `q=0` exclusion, `text/*`, `*/*`, malformed Accept treated as absent,
  unknown extension, no-match 406 body, `Vary` present only for Accept decisions.
- `tests/nativeweb.web.tests.unit/PublicPageCacheTests.cs` — a negotiated route under the policy
  on the existing `PublicPagePipeline` host: HTML and JSON of one path served from the cache
  each under its own type; locale variance unchanged.
- `tests/nativeweb.scaffold.tests.integration/PublicFormatsTests.cs` — every sample URL on both
  stores (`SampleFactory`, `SampleJauntyFactory`): status, content type, length, nosniff, the
  JSON parsing to the seeded rows, robots naming the sitemap, feed links absolute against the
  test host, the antiforgery cookie absent on non-HTML responses.
- `tests/nativeweb.scaffold.tests.e2e/PublicFormatsTests.cs` — Chromium against
  `InMemorySampleFixture`: `page.GotoAsync("/articles.json")` response header and body;
  `/feed.atom` served as `application/atom+xml`; an `APIRequestContext` call with
  `Accept: application/json` on `/articles` receiving JSON with `Vary: Accept`.
- `tests/nativeweb.repo.tests.unit/DocsClaimsTests.cs` — unchanged; it already sweeps the two
  READMEs and the architecture doc for path claims, so the rewrite is checked for free.

Each new test is broken once (wrong media type, escaping removed, precedence flipped) after the
green commit and shown red, then reverted with `git checkout --`.

## Data / interfaces

```csharp
public readonly record struct Format(string Extension, string MediaType, bool Text)
{
    public static readonly Format Html, Text, Markdown, Json, Xml, Atom, Rss, Csv;
    public string ContentType { get; }                  // "text/csv; charset=utf-8"
    public static bool TryFromExtension(string ext, out Format f);
}

public static class FormatResults
{
    IResult Text(string body);                            // text/plain
    IResult Markdown(string body);                        // text/markdown
    IResult Json<T>(T value, JsonTypeInfo<T> typeInfo);   // application/json
    IResult Xml(Action<XmlWriter> write);                 // application/xml
    IResult Atom(Feed feed, Uri baseUrl);                 // application/atom+xml
    IResult Rss(Feed feed, Uri baseUrl);                  // application/rss+xml
    IResult Csv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows);
    IResult Sitemap(IEnumerable<SitemapEntry> entries, Uri baseUrl);
}

public sealed record Feed(string Id, string Title, string Link, DateTimeOffset Updated,
    IReadOnlyList<FeedEntry> Entries, string? Author = null, string? Subtitle = null);
public sealed record FeedEntry(string Id, string Title, string Link, DateTimeOffset Updated,
    string? Summary = null, string? Author = null);
public sealed record SitemapEntry(string Location, DateTimeOffset? LastModified = null,
    string? ChangeFrequency = null, double? Priority = null);

public static Uri PublicBase(this HttpRequest request);   // scheme + host, after forwarding

public static NegotiatedRouteBuilder MapNegotiated(this IEndpointRouteBuilder endpoints,
    string pattern, IReadOnlyList<Format> offers,
    Func<HttpContext, Format, ValueTask<IResult>> handler);
```

Negotiation contract, in order: route value `format` present → `TryFromExtension` and membership
in `offers`, else 404. Otherwise Accept present and parseable → best offer by `q` then
specificity, `q=0` excluded; wildcard or absent → `offers[0]`; nothing acceptable → 406 with
`text/plain` body `Offered: text/html, application/json`. `Vary: Accept` only on the Accept
path. `HttpContext.Items["nativeweb.format"]` holds the choice for the cache policy and for
tests.

## Risks & mitigations

- **Route template.** `/articles.{format}` is a complex segment with a literal prefix. Routing
  documents `{file}.{ext?}`; the literal-prefixed form is expected to work, and task 1 of the
  negotiation work proves it with a test before anything is built on it. Fallback: the
  extension route is `"{pattern}.{format:regex(^[a-z]+$)}"` mapped separately, which is the same
  shape either way. The existing `POST /articles` is unaffected: different method.
- **Antiforgery cookie on the HTML branch.** The sample's HTML listing calls
  `GetAndStoreTokens`, which sets a cookie, so the default output-cache policy refuses to store
  it (spec 006 R2). JSON, CSV, feed and sitemap responses set no cookie and are stored. The R7
  proof of "each representation under its own type" is therefore on the framework's own test
  host with two cookie-free representations, and in the sample by the JSON listing being stored
  while the HTML one is not. The plan does not remove the token from the listing page: that is
  the next spec's public-site work.
- **CSV in spreadsheets.** The single-quote prefix is the OWASP mitigation and changes the cell
  text a reader sees; the numeric exemption keeps `-5` a number. Documented in the README as the
  trade the helper makes. Charset `utf-8` without BOM: Excel on Windows may guess wrong, which is
  a documented limitation, not a bug to fix here.
- **Feed links.** `PublicBase` trusts `Request.Scheme` and `Request.Host`, which are only right
  when `UseForwardedHeaders` ran with the configured known proxies. That is the pipeline's
  existing contract (`ForwardedHeaders:KnownProxies`), and a caller can pass its own base.
- **Cache poisoning across formats.** The `format` vary value comes from the pure chooser, never
  from the raw header, and extension URLs are distinct paths. A test on the framework host
  asserts a JSON body is never served to an HTML request of the same path.
- **406 bodies.** Text only, naming media types the route already advertises. No reflection of
  the request's Accept string into the body.
- **AOT.** No reflection anywhere: `JsonTypeInfo<T>` is required, `XmlWriter` is a stream
  writer, and the handler shape is a plain delegate. `dotnet publish -c Release` of the sample
  is part of the verify step, and it fails the build on any trim warning.

## Coverage check

| R# | Plan section |
|---|---|
| R1 | `Format.cs`, `FormatResults.cs`; unit tests on media type, charset, length |
| R2 | Tech (source-generated JSON, XmlWriter); `SampleJson.cs`; Release publish in verify |
| R3 | `Csv.cs`, XmlWriter escaping, JSON serializer; round-trip tests |
| R4 | `Feed.cs`, `RequestBase.cs`; feed and sitemap tests including the vendored XSD |
| R5 | `Negotiation.cs`, `NegotiatedEndpoints.cs`; precedence tests |
| R6 | `Negotiation.cs` refusals; 404 and 406 tests |
| R7 | `Vary` in `NegotiatedEndpoints.cs`; `PublicPageCachePolicy.cs`; cache tests (see risk); the spec's R7 criterion narrowed to the cookie-free representations, recorded in the spec |
| R8 | pipeline unchanged; integration tests assert nosniff and the rate-limit policy |
| R9 | `PublicArticleEndpoints.cs`, `PublicDocuments.cs`, `Program.cs`; integration and e2e |
| R10 | architecture doc, decision 009, two READMEs, docs-site regen, DocsClaimsTests |
| R11 | Tests section; the break-and-revert step per new test |

No uncovered requirement. One interpretation recorded: R4's "validates against the respective
format's schema" is met for the sitemap by XSD validation and for Atom and RSS by the required
elements of RFC 4287 and RSS 2.0 checked with `XDocument`, because neither feed format has an
official XSD and the reference reader package is not available offline.
