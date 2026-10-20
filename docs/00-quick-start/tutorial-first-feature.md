# Tutorial — build your first feature

One feature, through every layer it touches: a read model, a JauntyQ query, a template, an
endpoint, and a test at each of the three tiers. Nothing is deferred to a later page, because the
layers only make sense against each other.

**The finished feature is already in this repository.** It is `GET /articles` in `apps/sample` — a
public, read-only page listing published articles, newest first, with a filter. Run it now, before
reading further:

```bash
dotnet run --project apps/sample
```

then open `http://localhost:5399/articles`. You are looking at the result of every step below.
The home page at `/`, the article page at `/articles/{slug}` and the tag pages under `/tags` are
the same pattern applied again: a reader interface, a template, a route. Read
`apps/sample/PublicSite.cs` once this walk-through makes sense.

**Follow along in your own product, not here.** If you have a stamped product from the
[getting-started guide](getting-started.md), build the equivalent page against its own table and
read this repository's files as the worked answer. Or just read: the layering is the same, and
`apps/sample` is the smaller place to see it whole.

The files, in the order this page covers them:

| File | Layer |
|---|---|
| `apps/sample/PublishedArticles.cs` | the read model and its seam, plus both implementations |
| `db/sample/queries/Articles/ListPublished.sql` | the query |
| `apps/sample/templates/articles/index.html` | the page template |
| `apps/sample/templates/articles/results.html` | the list fragment the page and the filter share |
| `apps/sample/PublicArticleEndpoints.cs` | the endpoint |
| `apps/sample/Program.cs` | the wiring |
| `tests/nativeweb.sample.tests.unit/PublicArticlesTests.cs` | unit tier |
| `tests/nativeweb.scaffold.tests.integration/PublicFeedTests.cs` | integration tier |
| `tests/nativeweb.scaffold.tests.e2e/PublicFeedTests.cs` | e2e tier |

## Why this feature is hand-written at all

`apps/sample` already has a complete admin UI over the same table, and not one line of it exists in
source: `app.MapScaffolding()` generates every route from the attributes on the `Article` model. So
the first question a tutorial owes you is why this page is not generated too.

Because scaffolding earns its place on CRUD over a table, and nothing else. The admin screens are
forms over columns, which is exactly the shape an attribute can describe. A public feed is a
product decision — which rows, in what order, showing which fields, at what page size — and none of
that is a property of the table. **A real product is mostly the hand-written kind.** The scaffolding
is there so the CRUD costs you nothing, not so the product does.

What scaffolding can and cannot express is in
[`libs/nativeweb.scaffold/README.md`](../../libs/nativeweb.scaffold/README.md).

## 1. The read model and its seam

`apps/sample/PublishedArticles.cs` opens with a record and an interface:

```csharp
public readonly record struct PublishedArticle(
    string Id,
    string Slug,
    string Title,
    string? Author,
    DateTime CreatedAt,
    IReadOnlyList<TagRef> Tags,
    DateOnly? PublishedAt = null);

public interface IPublishedArticles
{
    ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct);
    // ...plus BySlugAsync, CountViewAsync, ByTagAsync and SitemapAsync for the other public pages
}
```

Seven fields, where the `Article` model has eleven: no `Body`, no `Status`, no `CategoryKey`, no
view count. **That narrowing is the point, not an optimisation.** `Article` is the scaffolding's
model: its shape is decided by what the admin form edits, so handing it to a public page means the page silently gains a field the day someone adds
one to the form. A read model that names exactly what it renders cannot drift that way — and
`Body` never reaching a list page is a decision you can see rather than one you have to trust.

The interface is a seam for the same reason `IScaffoldStore<T>` is one: `Program.cs` decides which
implementation answers, and nothing above it learns whether a database exists. Two implementations
sit below it in the same file.

**In memory**, filtering and sorting are the caller's job, because the store seam offers no way to
express either:

```csharp
return all
    .Where(a => a.Status == ArticleStatus.Published)
    .OrderByDescending(a => a.PublishedAt ?? DateOnly.FromDateTime(a.CreatedAt))
    .ThenByDescending(a => a.CreatedAt)
    .ThenByDescending(a => a.Id, StringComparer.Ordinal)
    .ToList();
```

`Status` is an enum with three members and only `Published` is public: a `Draft` and an `Archived`
row are equally absent from every public page, and the article page answers 404 for both. The
order is the publication date the admin set, a row without one dated by its creation day, so an
article can be published today and dated last week. The category the admin picked is not in the
read model at all, which is the same narrowing again: the public site does not show it yet, so it
does not carry it.

**Over PostgreSQL**, the same class does nothing but map rows, because the filter and the ordering
moved into SQL:

```csharp
List<Row> rows = await db.Articles.ListPublishedAsync(take, ct).ConfigureAwait(false);
```

Both are registered as singletons. Neither holds per-request state, and the JauntyQ one takes a
connection from the pool per call — the reasoning is in `JauntyArticleStore`'s remarks, next to it.

Keeping both is deliberate. The in-memory path is why the demo, the integration tier and the
browser tier all work with no database at all, and the pair is the clearest statement this
codebase makes that persistence is a registration rather than a rewrite.

## 2. The query

All data access goes through JauntyQ — no EF Core, no Dapper, no hand-written ADO.NET — and a
JauntyQ query is a `.sql` file that the source generator turns into a typed method. There is no
step where you write the C# for it. Drop the file in `db/sample/queries/Articles/`, build, and
`db.Articles.ListPublishedAsync` exists.

The generator, the schema snapshot it reads, and why `apps/sample` hosts it directly rather than
through a data library of its own, are in [`db/sample/README.md`](../../db/sample/README.md) and
[`db/README.md`](../../db/README.md). Read the first one now if you have not: it also has the two
`CREATE DATABASE` lines this feature's tests need.

```sql
-- @params take:int
SELECT id, title, author, body, view_count, created_at, slug, status, published_at, category_key
FROM articles
WHERE status = 'published'
ORDER BY COALESCE(published_at, (created_at AT TIME ZONE 'UTC')::date) DESC, created_at DESC, id DESC
LIMIT @take;
```

The committed file also carries a long comment; the query itself is the lines above. Two things in
it are worth the walk.

**`-- @params take:int`.** The generator infers a parameter's type by looking at the typed column it
is compared against. `@take` appears in `LIMIT`, where there is no column, so it infers nothing and
says so — `JNT4003` — rather than guessing. Declaring it is the fix.

**The `ORDER BY` has to match an index.** A query that sorts on columns no index covers sorts at
runtime, and JauntyQ raises `JNT8007` about it. JauntyQ rates that a Warning; `TreatWarningsAsErrors`
is on repo-wide (`AGENTS.md`), so **here it stops the build.** This query is covered by the partial
expression index `articles_public_order_idx` in
`db/sample/migrations/0004_status_dates_categories.sql`, which has the same three descending keys
over `status = 'published'`. A btree can be walked backwards but not half-backwards, so the
directions in the query must match the index's own.

When a table is small and will stay small, the other honest answer is to accept the sort with
`-- @allow-sort <reason>`. The reason is mandatory: a bare directive is rejected with `JNT3008`.
The exemption also expires on its own. Once an index covers the sort, `JNT8012` fires on the
directive and says it suppresses nothing, so an exemption cannot outlive what justified it.

The `JNT` diagnostics are JauntyQ's own and are documented upstream, not in this repository. The
`NWT` codes in [`template-diagnostics.md`](../02-reference/template-diagnostics.md) are a different family
belonging to the template engine, and do not apply here.

**Every column, though the page shows a few.** A whole-table projection makes the generated row type
`Extrode.JauntyQ.Generated.Article` — the same type `List.sql` already produces — so this query adds a
method and not a second row shape to learn. Narrow it when row width costs something; on a demo
table it buys nothing and costs a type.

**The order ends in `created_at DESC, id DESC`.** The leading key is the publication date, with a
row that has none dated by its creation day. `created_at` defaults to `NOW()`, so a scripted or
seeded insert can give several rows one timestamp. Without the `id` tiebreak the page order is
whatever the database happens to return: stable enough to look fine locally, unstable enough to
reorder between requests in production.

## 3. The template

A page is a compiled template. `apps/sample/templates/articles/index.html` declares its inputs in a
header comment, and the render generator turns the file into a typed method at build time:

```html
<!--#template
  @layout layout/page
  @param string Query
  @param int Take
  @param string AntiforgeryFieldName
  @param string AntiforgeryToken
  @param System.Collections.Generic.IReadOnlyList<NativeWeb.Sample.PublishedArticle> Articles
-->
<h1 class="page__title">{{t:articles.title}}</h1>
<p class="page__lede">{{t:articles.lede}}</p>
```

Templates are `AdditionalFiles` in `nativeweb.sample.csproj`; there is no view engine and no
runtime parsing. Call the generated `Templates.Articles.Index` with the wrong parameters and the
build fails, so **the compiler tells you a field is missing.** Build errors from the template
itself are listed in [`template-diagnostics.md`](../02-reference/template-diagnostics.md). Why the
architecture makes this trade — server-rendered HTML, no SPA build pipeline, everything compiled
into one NativeAOT binary — is [`native-web-architecture.md`](../01-architecture/native-web-architecture.md).

The list itself is a second template, `articles/results.html`, which the page includes with
`{{> articles/results Articles=Articles}}`. It is split out because the filter swaps it alone.

**Interpolations are encoded by default.**

```html
<h2 class="articles__title"><a href="/articles/{{article.Slug}}">{{article.Title}}</a></h2>
```

The title and the author come out of a database that an admin form writes to, which makes them
attacker-influenced input on a public page. `{{article.Title}}` is HTML-encoded without you asking.
A unit test asserts that this default is real (`Results_EncodesTitleAndAuthor`). The standard this
is held to is [`security-baseline.md`](../02-reference/security-baseline.md).

**The empty case says something.**

```html
{{if Articles.Count == 0}}
<p class="articles__empty">{{t:articles.empty}}</p>
{{else}}
```

An empty feed is a normal state — a fresh database has no published rows — and an empty `<ul>`
reads as a broken page. The nullable author gets the same treatment one level down:
`{{if article.Author != null}}` omits the element entirely, so an author-less article has no stray
punctuation.

**Text comes from the locale catalog.** `{{t:articles.title}}` reads `apps/sample/locales/en.json`.
Every key a template uses must exist in the root catalog.

**The page works without JavaScript.** The filter form carries a real `method="post"` and
`action="/articles"` as well as the htmx attributes:

```html
<form class="articles__filter" method="post" action="/articles"
      hx-post="/articles" hx-target="#results" hx-select="#results" hx-swap="outerHTML">
```

With JavaScript the response swaps only `#results`; without it the form posts and the whole page
comes back. [`progressive-enhancement.md`](../01-architecture/progressive-enhancement.md) is mandatory
for any page that gains an interaction.

## 4. The endpoint

`apps/sample/PublicArticleEndpoints.cs` maps the route with `MapNegotiated`, which serves one path
in several representations:

```csharp
endpoints.MapNegotiated("/articles", [Format.Html, Format.Json, Format.Csv], async (http, format) =>
{
    IPublishedArticles articles = http.RequestServices.GetRequiredService<IPublishedArticles>();
    IReadOnlyList<PublishedArticle> recent = await articles
        .RecentAsync(PageSize, http.RequestAborted).ConfigureAwait(false);

    if (format == Format.Json)
    {
        return FormatResults.Json(recent, SampleJson.Default.IReadOnlyListPublishedArticle);
    }

    if (format == Format.Csv)
    {
        return FormatResults.Csv(CsvHeaders, recent.Select(CsvRow));
    }

    IAntiforgery antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
    AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);
    return Page(http, recent, query: null, take: PageSize, tokens);
})
.RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy);
```

`/articles` answers the page, `/articles.json` and `/articles.csv` (or an `Accept` header naming
either type) answer the same rows as data, and any other extension is a 404 that never reaches the
store. The mechanism is in the "Negotiated routes" section of
[`libs/nativeweb.web/README.md`](../../libs/nativeweb.web/README.md).

`Page` renders through the compiled layout:

```csharp
return Templates.Articles.Index.Result(
    locale: locale,
    Title: locale["title.articles"],
    AntiforgeryToken: tokens.RequestToken ?? "",
    FlashOk: flash.Ok,
    FlashError: flash.Error,
    Query: query ?? "",
    Take: take,
    AntiforgeryFieldName: tokens.FormFieldName,
    Articles: articles);
```

The filter posts to the same path through `MapFormPost<ArticleFilter>("/articles", ...)`. The handler
takes the bound `ArticleFilter` and returns the `results` fragment alone for an htmx request
(`HX-Request` header) or the whole page for a plain form post. Same handler, same data, one branch.

Minimal APIs only — no MVC, no controllers, no Razor Pages. The extension-method shape
(`MapPublicArticles`) is the convention every route group in the repository follows.

**`PageSize` is a constant, not a query-string parameter.** An attacker-controlled page size is a
denial-of-service lever, and a public feed has no reason to offer one. The filter form does send a
`max`, so `ParseTake` falls back to `PageSize` for anything unparseable and caps the rest at 100. If
you need paging, take a cursor, not a count.

**There is no `RequireAuthorization`, and that is the deliberate half.** The GET changes nothing
and shows only rows already marked published, so it needs no authentication and no antiforgery
token. The admin routes under `/admin` sit behind sign-in and redirect an anonymous visitor to
`/signin`; this one must not, and adding the guard here would be the thing to reconsider rather
than omitting it. Two
tests below exist solely to hold that line, because an absent guard is invisible in a diff.

**There is no output cache on this route either.** The HTML embeds an antiforgery token for the
filter form, so a stored copy would be served to a visitor whose cookie cannot match it. A page
that carries no token can be cached; see `AsPublicPage` in `PublicDocuments.cs`.

## 5. The wiring

One line in `Program.cs`, next to the generated routes it contrasts with:

```csharp
app.MapScaffolding();
app.MapPublicArticles();
```

and one registration per branch of the store choice, which is where the whole feature's persistence
decision lives:

```csharp
// no connection string
builder.Services.AddSingleton<IPublishedArticles>(new MemoryPublishedArticles(articles, tags, relation));

// with one
builder.Services.AddSingleton<IPublishedArticles>(
    sp => new JauntyPublishedArticles(sp.GetRequiredService<NpgsqlDataSource>()));
```

The choice is made in the application, not in a library, and it is made from
`ConnectionStrings:Sample`. That key — and the environment-variable spelling
`ConnectionStrings__Sample` the test tiers and CI use — is in
[`configuration.md`](../02-reference/configuration.md).

## 6. The unit tier

`AGENTS.md` requires all three tiers for every feature, and the order in that requirement is not
decorative: **test each unit against its own inputs, failure paths included, and only then
holistically through the real entry point.** Both layers, not one standing in for the other.

The unit tier carries the most facts here, and it is the only tier that can. It states one rule per
test:

| Fact | Rule it pins |
|---|---|
| `RecentAsync_OmitsUnpublishedArticles` | a draft is not public |
| `RecentAsync_OmitsArchivedArticles` | an archived row is not public either |
| `RecentAsync_OrdersByPublicationDate_BeforeCreationTime` | the publication date leads the order |
| `RecentAsync_DatesARowWithoutAPublicationDateByItsCreationDay` | the fallback date |
| `RecentAsync_OrdersNewestFirst` | seeded out of order, so passing cannot be insertion order |
| `RecentAsync_WithIdenticalTimestamps_BreaksTheTieById` | the tiebreak the SQL spells out |
| `RecentAsync_TakesAtMostTheRequestedCount_KeepingTheNewest` | truncation happens after the sort |
| `Results_WithNoArticles_SaysSoRatherThanRenderingAnEmptyList` | the empty state |
| `Results_WithAnArticle_ShowsTitleAuthorAndDate` | the happy path |
| `Results_WithNoAuthor_OmitsTheAuthorElementEntirely` | the nullable field |
| `Results_EncodesTitleAndAuthor` | the encoding |
| `IndexIsAWholeDocumentAndResultsIsAFragment` | the page has the chrome, the swap target does not |
| `IndexEchoesTheSubmittedTakeRatherThanAConstant` | the form shows the value that produced the list |
| `Matching_FindsAnAuthorAsWellAsATitle` | the filter does what its placeholder says |

The integration tier could only ever say the page came back `200` with the right words on it. Only
here does a failure name the rule that broke rather than the page that did.

The template tests call the generated `Templates.Articles.*` methods directly, for example
`Templates.Articles.Results.ToHtmlString(locale: Locales.En, Articles: [...])`.

**`JauntyPublishedArticles` is absent from this tier on purpose.** Its whole body is one generated
call and a row-to-record loop, so a unit test of it would be a test of a mock. What it actually does
is a claim about PostgreSQL, and it gets asserted against PostgreSQL one tier up.

## 7. The integration tier

`WebApplicationFactory` against a real host, and this file has two halves because the feature has
two implementations. What is unproven until here: that the route exists, that it needs no sign-in,
and — for the JauntyQ half — that the SQL binds its parameter and applies its `WHERE` and
`ORDER BY` the way the C# above it assumes. **A LINQ expression and a SQL statement can agree in the
unit tier and disagree against PostgreSQL.**

Three techniques from this file are worth taking with you.

**Arrange the negative case so a missing filter is loud.** The draft row is inserted *newer* than
the published one, so a missing `WHERE published` puts it first on the page rather than merely
somewhere on it:

```csharp
await SampleTestDatabase.InsertAsync($"{prefix}-p", $"{prefix}-published", "xunit", "body", published: true,  createdAt: future);
await SampleTestDatabase.InsertAsync($"{prefix}-d", $"{prefix}-draft",     "xunit", "body", published: false, createdAt: future.AddMinutes(1));
```

**Date rows into the future, and remove them afterwards.** The page shows the newest twenty and
`nativeweb_sample_test` accumulates rows across runs, so a row dated `NOW()` is only reliably on the
page while the suite is young. The suite does not truncate — every other test finds its row by
unique title at any position, so accumulation is harmless there — but a future-dated row left behind
would displace the *next* run's rows out of the page, and the suite would break itself after ten
runs. Hence a `finally` that deletes exactly the ids this test minted:

```csharp
finally
{
    await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
}
```

**Assert the open route by requesting it.** `Feed_IsReachableWithoutSigningIn` asks for a `200` and
for `<h1 class="page__title">Articles</h1>` in the body, with `AllowAutoRedirect = false`. Nothing but a request proves
which side of the authentication line a route landed on.

## 8. The end-to-end tier

Playwright, a real browser, the NativeAOT binary, real PostgreSQL. The usual argument for this tier
is that only a browser can drive a dialog or a script, and **this page has no JavaScript, so that
argument does not apply.** Two others do, and they are the ones to reason from when you are deciding
whether your own feature needs this tier.

**It is the only place the feature runs inside a NativeAOT binary.** Everything below it is a
JIT-hosted test process. `ListPublishedAsync` is generated code doing reflection-free mapping and
the compiled templates are generated code; either could work perfectly under the JIT and be
trimmed out of the published binary. A green integration tier says nothing about that.

**A browser follows redirects, and the tier below it cannot.** `AllowAutoRedirect = false` is the
right way to observe a guard and the wrong way to observe its absence. `Feed_IsReachedByABrowserWithNoSession`
checks the URL first, because a redirect to `/signin` would still render a page and an assertion on
the heading alone would not notice which page.

The encoding fact also changes meaning here. The tiers below check the HTML for `&lt;script&gt;`,
which is a claim about a string. Only here is there a real HTML parser and a real script engine to
be wrong about it:

```csharp
Assert.Equal(0, await page.Locator("li.articles__item script").CountAsync());
Assert.False(await page.EvaluateAsync<bool>("() => window.__pwned === true"));
```

An injected `script` element would be in the DOM, and every string assertion below would have passed
all the way up.

## 9. Perturb every green test

`AGENTS.md` again, and it is the rule most easily skipped: **a test that passes because the code
under it never executed asserts nothing.** When a new test passes on its first run, change the code
so it should fail, and confirm it does.

Perturb one thing at a time. Changing `WHERE` and `ORDER BY` together can fail more tests than
you expected, and a perturbation that fails a test by an unintended route proves nothing about that
test. Then read the failure message rather than counting failures: a test that goes red for the
wrong reason is as useless as one that goes green for the wrong reason. For example, adding
`.RequireAuthorization()` to this route fails the e2e guard test, and the message shows the browser
followed `/articles` to `/signin` to `/admin`.

## Run it all

```bash
dotnet test --project tests/nativeweb.sample.tests.unit
dotnet test --project tests/nativeweb.scaffold.tests.integration
dotnet test --project tests/nativeweb.scaffold.tests.e2e
```

The integration and e2e tiers need `nativeweb_sample_test` — [`db/sample/README.md`](../../db/sample/README.md)
has the two lines that create it, and the Playwright browser install is in
[getting-started § 4](getting-started.md).

## What to take to your own feature

- **Narrow the read model to what the page renders.** It is the cheapest defence against a UI model
  and a storage model becoming the same type.
- **Put the filter and the sort where the data is.** The in-memory implementation shows you what it
  costs when you cannot.
- **Let the diagnostics stop you.** `JNT8007` is a question about your schema, not an obstacle;
  answer it with an index or with a reason, and never with a blanket suppression.
- **Encode at the point of output, visibly.**
- **State each rule in the tier that can name it**, and only then assert the whole page.
- **Perturb it.** One change at a time, and read the message.

Reference material, once you need a specific answer rather than a path, is indexed in
[`docs/README.md`](../README.md).
