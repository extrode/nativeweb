# Tutorial — build your first feature

One feature, through every layer it touches: a read model, a JauntyQ query, a renderer, an
endpoint, and a test at each of the three tiers. Nothing is deferred to a later page, because the
layers only make sense against each other.

**The finished feature is already in this repository.** It is `GET /articles` in `apps/sample` — a
public, read-only page listing published articles, newest first. Run it now, before reading
further:

```
dotnet run --project apps/sample
```

then open `http://localhost:5399/articles`. You are looking at the result of every step below.

**Follow along in your own product, not here.** If you came through
[Path A of the getting-started guide](getting-started.md), you have a stamped product with its own
table; build the equivalent page against that, and read this repository's files as the worked
answer. There used to be a second path here, for the identity provider this repository held; it
left on 2026-08-31, so there is one path. Read rather than type if you only want the layering — it is
identical and `apps/sample` is the smaller place to see it whole.

The files, in the order this page covers them:

| File | Layer |
|---|---|
| `apps/sample/PublishedArticles.cs` | the read model and its seam, plus both implementations |
| `db/sample/queries/Articles/ListPublished.sql` | the query |
| `apps/sample/Render/ArticlesPage.cs` | the renderer |
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
public readonly record struct PublishedArticle(string Id, string Title, string? Author, DateTime CreatedAt);

public interface IPublishedArticles
{
    ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct);
}
```

Four fields, where the `Article` model has seven. **That narrowing is the point, not an
optimisation.** `Article` is the scaffolding's model: its shape is decided by what the admin form
edits, so handing it to a public page means the page silently gains a field the day someone adds
one to the form. A read model that names exactly what it renders cannot drift that way — and
`Body` never reaching a list page is a decision you can see rather than one you have to trust.

The interface is a seam for the same reason `IScaffoldStore<T>` is one: `Program.cs` decides which
implementation answers, and nothing above it learns whether a database exists. Two implementations
sit below it in the same file.

**In memory**, filtering and sorting are the caller's job, because the store seam offers no way to
express either:

```csharp
return all
    .Where(a => a.Published)
    .OrderByDescending(a => a.CreatedAt)
    .ThenBy(a => a.Id, StringComparer.Ordinal)
    .Take(take)
    .Select(a => new PublishedArticle(a.Id, a.Title, a.Author, a.CreatedAt))
    .ToList();
```

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
-- @allow-sort demo table, three seeded rows; an index would cost more to explain than the sort costs to run
SELECT id, title, author, body, published, view_count, created_at
FROM articles
WHERE published
ORDER BY created_at DESC, id
LIMIT @take;
```

Four things in six lines, and the second is the one worth the walk.

**`-- @params take:int`.** The generator infers a parameter's type by looking at the typed column it
is compared against. `@take` appears in `LIMIT`, where there is no column, so it infers nothing and
says so — `JNT4003` — rather than guessing. Declaring it is the fix.

**`-- @allow-sort`, and the diagnostic that forces it.** `articles` carries exactly one index, the
`id` primary key. `ORDER BY created_at` therefore sorts at runtime, and JauntyQ raises `JNT8007`
about it. JauntyQ rates that a Warning; `TreatWarningsAsErrors` is on repo-wide (`AGENTS.md`), so
**here it stops the build.**

You have two honest answers and you must pick one:

| Answer | When it is right |
|---|---|
| Add an index on `created_at` in a migration | The table grows. This is the usual answer. |
| Accept the sort and say why | The table does not grow. |

This table is three seeded rows in a demo, where PostgreSQL would ignore the index anyway, so the
second is the true answer and `@allow-sort` is how you record it. **The reason is mandatory** — a
bare directive is rejected outright with `JNT3008`, so the exemption cannot be a shrug.

It is also self-expiring. Add an index whose leading column is `created_at` and `JNT8012` fires on
that very line, saying the directive now suppresses nothing. An exemption cannot quietly outlive
the thing that justified it — which is what makes accepting the sort a decision rather than a
suppression.

The `JNT` diagnostics are JauntyQ's own and are documented upstream, not in this repository. The
`NWT` codes in [`template-diagnostics.md`](../02-reference/template-diagnostics.md) are a different family
belonging to the template engine, and do not apply here.

**Every column, though the page shows three.** A whole-table projection makes the generated row type
`JauntyQ.Generated.Article` — the same type `List.sql` already produces — so this query adds a
method and not a second row shape to learn. Narrow it when row width costs something; on a demo
table it buys nothing and costs a type.

**`ORDER BY created_at DESC, id`.** `created_at` defaults to `NOW()`, so a scripted or seeded insert
can give several rows one timestamp. Without the tiebreak the page order is whatever the database
happens to return: stable enough to look fine locally, unstable enough to reorder between requests
in production.

## 3. The renderer

There is no view engine and no template file. A page is a static method that returns a string:

```csharp
internal static class ArticlesPage
{
    public static string Render(IReadOnlyList<PublishedArticle> articles) { ... }
}
```

Why the architecture makes that trade — server-rendered HTML, no SPA build pipeline, everything
compiled into one NativeAOT binary — is [`native-web-architecture.md`](../01-architecture/native-web-architecture.md),
which is the document the rest of this repository assumes. The immediate consequence for you is
smaller and more useful: **the type system is what tells you a field is missing.** A renderer that
does not compile is a page that cannot be half-written.

Two decisions inside it are worth copying.

**The encoder is called explicitly.**

```csharp
HtmlEncoder encoder = HtmlEncoder.Default;
string title = encoder.Encode(article.Title);
```

Not because a template engine's automatic encoding would be worse, but because there isn't one, and
that changes where the risk lives: with no default, the one place raw markup could enter is
visible by grep. Nothing on this page is trusted — the title and the author come out of a database
that an admin form writes to, which makes them attacker-influenced input on a public page. The
standard this is held to is [`security-baseline.md`](../02-reference/security-baseline.md).

**The empty case says something.**

```csharp
if (articles.Count == 0)
{
    items.Append("<p class=\"articles__empty\">No published articles yet.</p>");
}
```

An empty feed is a normal state — a fresh database has no published rows — and an empty `<ul>` reads
as a broken page. The nullable `author` gets the same treatment one level down: omitted entirely
rather than rendered as an empty `<span>`, so an author-less article has no stray punctuation.

**This page carries no JavaScript at all**, which is why HTMX and Alpine do not appear above. That
is a property of this feature, not permission to skip the rules: the moment a page gains an
interaction, [`progressive-enhancement.md`](../01-architecture/progressive-enhancement.md) is mandatory, and it
requires the page work without JavaScript first — which this one does by construction.

## 4. The endpoint

```csharp
internal static class PublicArticleEndpoints
{
    private const int PageSize = 20;

    public static IEndpointRouteBuilder MapPublicArticles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/articles", async (IPublishedArticles articles, CancellationToken ct) =>
        {
            IReadOnlyList<PublishedArticle> recent = await articles.RecentAsync(PageSize, ct).ConfigureAwait(false);
            return Results.Content(ArticlesPage.Render(recent), "text/html");
        });

        return endpoints;
    }
}
```

Minimal APIs only — no MVC, no controllers, no Razor Pages. The handler is four lines because the
three layers under it each did one job, and the extension-method shape is the convention every
route group in the repository follows.

**`PageSize` is a constant, not a query-string parameter.** An attacker-controlled page size is a
denial-of-service lever, and a public feed has no reason to offer one. If you need paging, take a
cursor, not a count.

**There is no `RequireAuthorization`, and that is the deliberate half.** It is a GET that changes
nothing and shows only rows already marked published, so it needs no authentication and no
antiforgery token. Every other article route in this app redirects an anonymous visitor to
`/signin`; this one must not, and adding the guard here would be the thing to reconsider rather
than omitting it. Two tests below exist solely to hold that line, because an absent guard is
invisible in a diff.

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
builder.Services.AddSingleton<IPublishedArticles>(new MemoryPublishedArticles(articles));

// with one
builder.Services.AddSingleton<IPublishedArticles>(new JauntyPublishedArticles(dataSource));
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
| `RecentAsync_OrdersNewestFirst` | seeded out of order, so passing cannot be insertion order |
| `RecentAsync_WithIdenticalTimestamps_BreaksTheTieById` | the tiebreak the SQL spells out |
| `RecentAsync_TakesAtMostTheRequestedCount_KeepingTheNewest` | truncation happens after the sort |
| `Render_WithNoArticles_SaysSoRatherThanRenderingAnEmptyList` | the empty state |
| `Render_WithAnArticle_ShowsTitleAuthorAndDate` | the happy path |
| `Render_WithNoAuthor_OmitsTheAuthorElementEntirely` | the nullable field |
| `Render_EncodesTitleAndAuthor` | the encoding |

The integration tier could only ever say the page came back `200` with the right words on it. Only
here does a failure name the rule that broke rather than the page that did.

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
for `<h1>Articles</h1>` in the body, with `AllowAutoRedirect = false`. Nothing but a request proves
which side of the authentication line a route landed on.

## 8. The end-to-end tier

Playwright, a real browser, the NativeAOT binary, real PostgreSQL. The usual argument for this tier
is that only a browser can drive a dialog or a script, and **this page has no JavaScript, so that
argument does not apply.** Two others do, and they are the ones to reason from when you are deciding
whether your own feature needs this tier.

**It is the only place the feature runs inside a NativeAOT binary.** Everything below it is a
JIT-hosted test process. `ListPublishedAsync` is generated code doing reflection-free mapping and
`HtmlEncoder.Default` is resolved at runtime; either could work perfectly under the JIT and be
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

Every one of the nineteen facts above was perturbed before being kept. Two findings from doing it
are worth more than the rule:

**Perturb one thing at a time.** Changing `WHERE` and `ORDER BY` together failed four integration
tests instead of the two expected — `ORDER BY ASC` had pushed the future-dated rows off the
twenty-row page. A perturbation that fails a test by an unintended route proves nothing about that
test. Re-run singly, each failed alone.

**Read the failure, do not count it.** Adding `.RequireAuthorization()` failed the e2e guard test in
96 ms, fast enough to suspect the host had never started. The message settled it —
`Assert.EndsWith() Failure … String: "http://localhost:5348/admin"` — the browser really had
followed `/articles` → `/signin` → `/admin`. A red test for the wrong reason is the same as a green
test for the wrong reason.

## Run it all

```
dotnet test tests/nativeweb.sample.tests.unit
dotnet test tests/nativeweb.scaffold.tests.integration
dotnet test tests/nativeweb.scaffold.tests.e2e
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

---

**Verified.** Every command and file reference on this page was executed or checked in the form
written, on 2026-08-25 — Windows 11 (10.0.26200.8457), .NET SDK 10.0.203, PostgreSQL 18.2,
PowerShell 7.6.4. The feature was built and its three tiers run on that machine before this page was
written: `tests/nativeweb.sample.tests.unit` 16 facts,
`tests/nativeweb.scaffold.tests.integration` 31, `tests/nativeweb.scaffold.tests.e2e` 13 — 0 failed,
0 skipped. Counts drift as the repository grows; a failure is the thing that matters.
