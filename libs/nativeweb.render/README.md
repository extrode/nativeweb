# nativeweb.render

HTML rendering for NativeWeb: a source-generated template system that writes UTF-8
into an `IBufferWriter<byte>` with no intermediate whole-page string, plus the locale
catalogs and per-request negotiation those templates read from. This project is the
**runtime** half. The **build-time** half is `libs/nativeweb.render.generator`, which
turns a product's own `templates/**/*.html` and `locales/*.json` files into C#. A
consumer references both:

```xml
<ProjectReference Include="..\..\libs\nativeweb.render\nativeweb.render.csproj" />
<ProjectReference Include="..\..\libs\nativeweb.render.generator\nativeweb.render.generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Templates and locale catalogs are `AdditionalFiles` of the product project; the
generator never reads disk, only what the compiler hands it.

## The whole API

| Entry point | What it does |
| --- | --- |
| `Html.Raw` | Marks a string as already-encoded markup: the one unencoded path for values |
| `TemplateWriter` | The render target generated template code writes into |
| `TemplateRender` | The delegate a template body compiles to |
| `TemplateResults.Html` | Wraps a `TemplateRender` as an `IResult`: buffered, length declared, one write |
| `TemplateResults.ToHtmlString` | Materialises the same body as a string, for e-mail and tests |
| `FormatResults` | One `IResult` per text format: `Text`, `Markdown`, `Json`, `Xml`, `Csv`, `Atom`, `Rss`, `Sitemap` |
| `Format` | A representation: extension, media type, and whether it takes a charset |
| `Feed`, `FeedEntry`, `SitemapEntry` | The structured input `Atom`, `Rss` and `Sitemap` write |
| `RequestBase.PublicBase` | The request's scheme and host as the base every relative link resolves against |
| `LocaleCatalog` | One locale's fully resolved string table |
| `LocaleNegotiation` | `UseLocaleNegotiation` middleware and the `GetLocale` reader |
| `LocaleNegotiationOptions` | Configures that middleware |

Encoding is decided by overload resolution on `TemplateWriter`, at compile time:

| Overload | Behaviour |
| --- | --- |
| `Write(string?)` | HTML-encodes. Null renders nothing |
| `Write(Html)` | Writes pre-encoded markup verbatim |
| `Write` on `char`, `int`, `long` | No encoding; numbers cannot need it |
| `WriteRaw` | Generated code only, hidden from editor completion |

`Write(string?)` encodes the five HTML-significant characters (less-than, greater-than,
ampersand, both quotes) and lets non-ASCII text through as UTF-8 bytes rather than
numeric entities. A template expression of any other type fails to compile, which is
the intended default for types nobody has made an encoding decision about.

`LocaleCatalog` exposes `Locale` (the lowercase id), `Lang` (BCP-47 for the html lang
attribute), `Dir` ("ltr" or "rtl"), a string indexer, and placeholder substitution:

```csharp
Format(string key, params ReadOnlySpan<(string Name, string? Value)> args)
```

`Format` makes a single left-to-right scan: placeholders occurring in a substituted
value are never re-substituted, so argument order cannot splice one argument into
another. Placeholders with no matching argument are left in place.

`LocaleNegotiationOptions`:

| Property | Meaning |
| --- | --- |
| `SupportedLocales` | Lowercase locale ids the app ships catalogs for |
| `DefaultLocale` | When nothing else matches; "en" by default |
| `CookieName` | The override cookie; "locale" by default |
| `UserPreference` | Signed-in user's stored preference (users.locale), consulted first |

`UseLocaleNegotiation` chooses user preference, then cookie, then Accept-Language
(ordered by descending q, "*" excluded), then the default, and parks the result in
`HttpContext.Items` for `GetLocale`. Dialects fall back to their base language before
the next candidate is tried: "en-gb" matches "en" when only "en" is shipped.

## A page, a layout, and the endpoint that serves it

`templates/shared/layout.html` — a template containing a slot is a layout, and gets no
class of its own:

```html
<!--#template
     @param string title
-->
<!doctype html>
<html lang="{{locale.Lang}}" dir="{{locale.Dir}}">
  <head>
    <meta charset="utf-8">
    <title>{{title}}</title>
  </head>
  <body>
    {{slot Main}}
  </body>
</html>
```

`templates/articles/index.html`:

```html
<!--#template
     @layout shared/layout
     @param Html featured
     @param IReadOnlyList<Article> articles
-->
<h1>{{t:articles.heading}}</h1>
{{featured}}
<ul>
{{for article in articles}}
  <li>{{article.Title}}</li>
{{/for}}
</ul>
```

`locales/en.json` — nested objects flatten to dot-joined keys, and every `{{t:...}}`
key anywhere must exist in this root file:

```json
{
  "articles": {
    "heading": "Articles"
  }
}
```

`locales/ms.json` — an override file refines root keys; fallthrough to English is
resolved at build time, never walked at render time:

```json
{
  "articles": {
    "heading": "Artikel"
  }
}
```

The generator emits a static class per page, each path segment Pascal-cased and
nested under `Templates`, so the page above becomes `Templates.Articles.Index`. A page
whose layout reads `locale.Lang` takes the resolved catalog as an implicit first
parameter, layout parameters first and page parameters after:

```csharp
app.UseLocaleNegotiation(new LocaleNegotiationOptions
{
    SupportedLocales = Locales.All,
    UserPreference = context => currentUser.LocalePreferenceAsync(context),
});

app.MapGet("/articles", async (HttpContext context, ArticleStore store) =>
{
    LocaleCatalog locale = Locales.Get(context.GetLocale());
    IReadOnlyList<Article> articles = await store.RecentAsync(20);
    return Templates.Articles.Index.Result(
        locale,
        featured: Html.Raw(store.FeaturedMarkup()),
        articles: articles);
});
```

`Result` returns the buffered `IResult`; `ToHtmlString` returns the same markup as a
string for the paths that still compose one.

## Other formats

The same buffered, length-declared, one-write shape as `TemplateResults.Html`, for
everything that is not a page (spec 021):

```csharp
app.MapGet("/export.csv", async (IPublishedArticles articles, CancellationToken ct) =>
{
    IReadOnlyList<PublishedArticle> rows = await articles.RecentAsync(100, ct);
    return FormatResults.Csv(
        ["id", "title", "author"],
        rows.Select(r => new[] { r.Id, r.Title, r.Author ?? "" }));
});

app.MapGet("/feed.atom", async (IPublishedArticles articles, HttpRequest request, CancellationToken ct) =>
{
    IReadOnlyList<PublishedArticle> rows = await articles.RecentAsync(20, ct);
    Feed feed = new(
        Id: new Uri(request.PublicBase(), "/feed").AbsoluteUri,
        Title: "Articles",
        Link: "/articles",
        Updated: rows.Count > 0 ? rows[0].CreatedAt : DateTimeOffset.UnixEpoch,
        Entries: rows.Select(r => new FeedEntry(
            Id: new Uri(request.PublicBase(), "/articles/" + r.Id).AbsoluteUri,
            Title: r.Title,
            Link: "/articles/" + r.Id,
            Updated: r.CreatedAt)).ToList());
    return FormatResults.Atom(feed, request);
});
```

| Helper | Emits |
| --- | --- |
| `Text`, `Markdown` | `text/plain` and `text/markdown`, UTF-8, no BOM; `Text` takes a status code for refusals |
| `Json<T>` | Through the `JsonTypeInfo<T>` the caller supplies, so nothing reflects and nothing trims away |
| `Xml` | An `XmlWriter` over the buffer, declaration included; `WriteRaw` on it is the one unescaped path |
| `Csv` | RFC 4180: CRLF, fields quoted when they hold a comma, quote or line break, quotes doubled |
| `Atom`, `Rss` | RFC 4287 and RSS 2.0 from a `Feed`; RFC 3339 and RFC 822 dates in UTC |
| `Sitemap` | sitemaps.org 0.9 from `SitemapEntry` values; priority outside 0..1 throws |

`Csv` neutralises spreadsheet formulas: a cell whose first printable character is `=`, `+`,
`-` or `@` is prefixed with an apostrophe and quoted, unless the whole cell parses as a
number, so `-5` survives and `=CMD()` does not. Leading spaces and invisible characters are
skipped before that test, which is where the known bypasses hide.

Feed and sitemap links are written relative and resolved against a base: the `HttpRequest`
overloads take it from the request through `PublicBase`, after `UseForwardedHeaders` has
corrected the scheme and host; the `Uri` overloads take it from the caller. Choosing a
representation from the path or the `Accept` header is `MapNegotiated` in `nativeweb.web`.

## The generator

`RenderTemplateGenerator` is an incremental generator. A consumer references it as an
analyzer and never calls anything it defines, so it gets no README of its own. From
each product's own files it emits:

| Source | Emission |
| --- | --- |
| `templates/**/*.html` | A static class per page: `Render` into a `TemplateWriter`, plus `ToHtmlString`, `Result`, and `InlineScriptCspHashes` |
| `locales/*.json` | The `Locales` class: one `LocaleCatalog` per locale id, `Get`, `All` |

`InlineScriptCspHashes` lists 'sha256-…' script-src sources for the page's static
inline scripts, taken over the bytes `Render` writes, so a cacheable response needs no
per-response nonce. It is always emitted, empty included, so consumers can reference
it unconditionally. Generated classes land under the project's root namespace
(`build_property.RootNamespace`).

Every template mistake is a build error, not a runtime blank, and each diagnostic
points at the line in the template file that wrote it:

| Diagnostic | What it rejects |
| --- | --- |
| `NWT001` | Malformed template header, including a layout that chains to another layout |
| `NWT002` | Unknown directive in the header; only `@layout` and `@param` exist |
| `NWT003` | `@param` that is not "`@param <type> <name>`" |
| `NWT004` | `@layout` naming no template file |
| `NWT005` | A partial naming no template file |
| `NWT006` | Partial arguments not matching the partial's `@param` list |
| `NWT007` | Slots and fills misused: duplicate fill, fill outside a layout, unfilled slot name |
| `NWT008` | Unbalanced `{{if}}`, `{{for}}` or `{{fill}}` blocks |
| `NWT009` | Two templates compiling to the same class name |
| `NWT010` | A parameter declared with different types in page and layout |
| `NWT011` | A required layout slot the page never fills |
| `NWT012` | An expression carrying `;`, `{` or `}` — expressions are values, not statements |
| `NWT013` | A `{{t:...}}` key missing from `locales/en.json` |
| `NWT014` | A locale override for a key the English root never defined (typo) |
| `NWT015` | Malformed locale catalog: bad JSON, duplicate locale id, missing root, a `_dir` that is not ltr/rtl |
| `NWT016` | Interpolation inside a tag but outside a quoted attribute value |

`NWT016` is the check behind the writer's encoding contract: `Write(string?)` is safe
in text and inside a quoted attribute value and nowhere else, because an unquoted
value ends at a space and a space is not encoded. Quote the attribute value.

## What this library does not do

No template is parsed at run time. There is no engine to load, no path resolution, no
reflection over models; what is left at run time is generated C# writing bytes.

There is one unencoded path for values, `Html.Raw`, and the caller owns the claim that
the markup is safe. Auditing raw output means grepping for `Html.Raw` in application
code and `WriteRaw` in generated code. URL values from user data need scheme
validation in the calling C#: no HTML encoder stops a javascript: href.

`TemplateResults.Html` is for pages. It renders into a private buffer, declares the
length, and hands the body over in one write, because a chunked page costs 35% of its
throughput for framing no page needs. Every `FormatResults` helper buffers the same way.
A body that cannot know its length needs a different result type, one that streams.

`FormatResults` writes documents; it does not decide which one to write. Negotiation is
`nativeweb.web`'s, and what a sitemap lists is the application's.

`LocaleCatalog` lookup never throws mid-render: a miss returns the key itself. Keys
are compile-checked against the English root, so a miss can only come from a
hand-written C# key.

`LocaleNegotiation` picks a catalog id and nothing else. It does not set thread
culture, format numbers or dates, and outside the middleware `GetLocale` returns "en".

RTL detection covers the shipped set (Arabic, Hebrew, Persian, Urdu and friends); a
locale outside it declares `_dir` in its own file or stays ltr. `Lang` casing handles
one- and two-subtag ids.

## Proved by

`tests/nativeweb.render.tests.unit`: `TemplateWriterTests` (the encoding decisions),
`TemplateHtmlResultTests` (the `IResult` path), `LocalizationTests` (catalogs,
fallthrough, negotiation), `GeneratedTemplateTests` and `FormattingTests` (generated
output), `InlineScriptHashTests`, `FormatResultsTests`, `CsvTests` and `FeedTests` (the
other formats, the sitemap validated against the vendored `sitemap.xsd`), and
`GeneratorDiagnosticsTests` with `DiagnosticsReferenceTests` over the NWT set above.
