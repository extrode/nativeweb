# nativeweb.scaffold

Declarative CRUD scaffolding for NativeWeb. Decorate a model; get an admin UI.

This project is the **runtime** half: the attributes, the `IScaffoldStore<T>` seam, and the
form-value converters the generated code calls. The **build-time** half is
`libs/nativeweb.scaffold.generator`. A consumer references both:

```xml
<ProjectReference Include="..\..\libs\nativeweb.scaffold\nativeweb.scaffold.csproj" />
<ProjectReference Include="..\..\libs\nativeweb.scaffold.generator\nativeweb.scaffold.generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## The whole API

```csharp
[Scaffold(Display = "Articles", Route = "articles")]
[Auth(Role = "admin", Delete = "owner")]
public sealed class Article
{
    [ScaffoldKey]                        public string Id { get; set; } = "";
    [ScaffoldList][ScaffoldSearch]
    [ScaffoldSort]                       public string Title { get; set; } = "";
    [ScaffoldMultiline]                  public string Body { get; set; } = "";
    [ScaffoldList]                       public bool Published { get; set; }
    [ScaffoldList][ScaffoldReadOnly]
    [ScaffoldSort]                       public DateTime CreatedAt { get; set; }
}
```

| Attribute | On | Effect |
| --- | --- | --- |
| `[Scaffold]` | class | Emits the routes. `Display` names the UI, `Route` the path segment |
| `[Auth]` | class | Role per operation; omitted means open |
| `[ScaffoldKey]` | property | The identity in every URL, and the tiebreaker in every order |
| `[ScaffoldList]` | property | A column. `Label` overrides the heading |
| `[ScaffoldSearch]` | property | Free-text search reaches it. No operators, no query language |
| `[ScaffoldSort]` | property | The list may be ordered by it, and its heading becomes a link |
| `[ScaffoldMultiline]` | property | `<textarea>` rather than `<input>` |
| `[ScaffoldReadOnly]` | property | Shown, never editable, no form input emitted |

Implement `IScaffoldStore<Article>` (six methods, no query language — JauntyQ owns data access),
register it, and call `app.MapScaffolding()`. That is the entire wiring. See `apps/sample`.

Routes emitted per model, under `/admin/{route}`:

| Method | Path | Operation | Role from |
| --- | --- | --- | --- |
| GET | `/` | list, search, sort, page | `Read` → `Role` |
| GET | `/new` | create form | `Write` → `Role` |
| POST | `/` | create | `Write` → `Role` |
| GET | `/{id}` | edit form | `Write` → `Role` |
| POST | `/{id}` | update | `Write` → `Role` |
| POST | `/{id}/delete` | delete | `Delete` → `Role` |

No `[Auth]` at all means the routes are open. That is a choice made by omission.

### Moving the UI off `/admin`

One assembly-level attribute, and every route, link, form action and redirect follows:

```csharp
[assembly: ScaffoldRoot("/manage")]
```

`/admin` is only the default. `"/"` is legal and puts the scaffolding at the site root; a trailing
slash is trimmed; anything that is not a path — no leading `/`, or carrying a query, fragment or
space — is **NWS002, a build error**, and nothing is emitted. A typo cannot quietly publish an
admin UI at a path nobody chose.

Compile-time rather than a `MapScaffolding(prefix)` argument, deliberately: it keeps each model's
base path a `const` so every emitted URL is a literal. A runtime prefix would have to be threaded
through every generated method or parked in static state, and static state is shared by two hosts
in one process — which is exactly what an integration suite is.

The role in `[Auth(Role = "admin")]` is unrelated and has always been yours: any role name, with
`Read`/`Write`/`Delete` overriding it per operation.

## Why a generator rather than reflection

NativeAOT bans the reflection every other ecosystem's scaffolding is built on: no
`Activator.CreateInstance`, no `PropertyInfo.SetValue`, no runtime model discovery. Measured in
`docs/brainstorms/001-framework-direction.md` §12, the platform's own declarative form binding is
closed too — a complex-type `[FromForm]` parameter emits `RDG003`, falls back to reflection, and
fails the build under `TreatWarningsAsErrors`; `[AsParameters]` compiles and then silently binds
every form field to null.

What does work is a type that binds itself through a compile-time static call:
`IBindableFromHttpContext<TSelf>`. That is what the generator emits, and everything below follows
from it.

**And one thing a generator has to know that a hand-written endpoint does not.** Measured
2026-08-19: the first NativeAOT publish of `apps/sample` failed with IL2026 and IL3050 on *every*
emitted `MapGet`/`MapPost`. The Request Delegate Generator only rewrites `Map*` calls it can see in
the compilation's own syntax trees, and Roslyn does not feed one generator's output to another — so
an emitted `Map*` call always binds the reflection-based `Map*(..., Delegate)` overload. The fix is
to emit `RequestDelegate`s instead (`ScaffoldEndpoints.Handle`), which carry neither attribute
because there is nothing left to bind. Verified: `dotnet publish -c Release -r win-x64` produces a
17 MB self-contained binary with zero trim or AOT warnings. **This applies to any NativeWeb
generator that emits endpoints**, not just this one.

## Three guarantees the generated code carries

**Mass assignment is closed by construction.** The emitted form is a `record` whose constructor
parameters are exactly the editable properties. A key or a `[ScaffoldReadOnly]` property is not a
parameter, so no request can reach it — there is no allowlist to keep in sync, because the only
way to bind a field is to declare it. Read-only values are carried from the existing row instead.

**Every mutating route re-attaches antiforgery metadata.** `[FromForm]` is what normally attaches
`IAntiforgeryMetadata`, so a `BindAsync`-bound handler has none and `UseAntiforgery()` skips the
route entirely — a tokenless POST measured at 200 OK. `RequiresAntiforgery.Instance` is emitted on
all three POSTs; the guard cannot be forgotten because nobody writes it.

**The content-type check lives inside `BindAsync`.** Argument binding runs *before* the
endpoint-filter pipeline, so a `RequireForm()` filter cannot protect a bind. The binder raises
`BadHttpRequestException(415)` itself.

## Sorting and paging, and the two things the store owes

The list orders by `?sort=` and `?dir=`, and pages by an opaque `?after=` cursor. A `?sort=` naming
anything outside the model's `[ScaffoldSort]` set falls back to the key ascending, so a client
cannot name an arbitrary column — the allow-list is compiled in, and in a JauntyQ store a validated
name selects *which static `.sql` file runs* rather than composing one.

A page is "the rows after this one in this order", not "rows 40 to 60". That is the point of the
cursor: with an offset, a row inserted ahead of a reader pushes one row from the page they are on
to the page they are about to request, and they never see it. The cost is that keyset paging cannot
express "page 7", which is why the pager is Previous / *N* items / Next.

`ScaffoldCursor` is **not a security boundary and is not signed.** It is opaque so that no client
builds one, not so that none can. A forged cursor repositions a reader inside rows they were
already permitted to list, and its values reach the database as parameters. What is guaranteed is
that no string in `?after=` can crash the list: `TryDecode` returns false for every malformed
shape, and the list renders from the beginning.

A `?sort=` naming nothing sortable takes the direction down with it — the fallback is the key
**ascending**, whatever `?dir=` said, because a direction applied to a column the reader did not
get is an ordering nobody asked for. No `?sort=` at all is different: that is a request for the
default column, and `?dir=desc` still applies to it.

**Three obligations the framework cannot enforce**, all stated on `IScaffoldStore<T>.ListAsync` and
all silent when broken:

1. **End every order with the key.** A page boundary inside a block of equal sort values has no
   single answer without a tiebreaker, and the rows sitting there repeat or vanish.
2. **Fix where NULLs go on a nullable sorted column,** in the query. `(author, id) > (@a, @id)` is
   `NULL` — not true — for an author-less row, so `WHERE` drops it and every page after the first
   omits every one of them. `db/sample/queries/Articles/ListByAuthorAsc.sql` carries the widened
   predicate. **This is invisible on a table small enough to fit one page**, which is why the
   reference app sorts by a nullable column on purpose.
3. **Return at most `take - 1` rows when you decline a backward cursor** and fall back to the
   first page. The list asks for one row more than the page to learn whether another exists, and
   it trims a backward page from the *tail* — reading the direction from the cursor it issued,
   which it cannot know the store ignored. A full `take` comes back as an overflowing backward
   page and its first row is trimmed away unseen. Both stores in this repo got this wrong first.

## Wiring, and how to opt out of it

Two extension methods stand in for the ceremony. Neither hides anything: both are a handful of
public calls, and writing those calls instead is a supported thing to do rather than a fallback.

```csharp
builder.Services.AddScaffolding();   // AddAntiforgery() + AddAuthorization()

app.UseScaffolding();                // the four calls below, in this order
```

`UseScaffolding()` expands to exactly:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseAntiforgeryRejection();
```

Write those four whenever the pipeline needs something between them — a rate limiter, a tenant
resolver, a request logger. **The order is the load-bearing part**: antiforgery reads endpoint
metadata so it must follow routing, and the rejection guard reads what antiforgery recorded so it
must follow antiforgery.

`UseAntiforgeryRejection()` is the one with behaviour rather than ceremony.
`UseAntiforgery()` validates but does **not** reject: it records the failure on
`IAntiforgeryValidationFeature` and calls the next middleware anyway. The request reaches the
endpoint, the form reader throws a bare `InvalidOperationException`, and the caller gets a 500
where a 400 belongs. The guard turns that into a 400 and, more importantly, stops the endpoint
running at all. Safe to register unconditionally — a GET carries no such feature, so it is a
no-op.

**Authentication is deliberately not in `AddScaffolding()`.** Cookie name, login path and role
membership are the application's security posture, and a default supplied by a library is one
nobody reviews. A host that calls `AddScaffolding()` and no `AddAuthentication` gets scaffolded
routes that 401 — the correct failure. `AddScaffolding_RegistersNoAuthenticationScheme` asserts
the omission so it cannot be helpfully added later.

## What the host still owns

Rate limiting. The scaffolded routes carry no policy.

## Running the demo

```
cd apps/sample && dotnet run
```

Then open <http://localhost:5399/signin>, which grants the admin role and redirects to `/admin`.
Every scaffolded `Article` route requires that role, so anywhere else is a 302 back to it. From
Visual Studio, F5 uses the same profile and opens the same URL.

**The demo is HTTP only, deliberately.** `WebApplication.CreateSlimBuilder` omits Kestrel's HTTPS
configuration — that is much of what makes it slim, since certificate loading drags in the
configuration binding an AOT publish would rather not have. Give it an `https://` address and it
throws on startup, measured 2026-08-19:

```
System.InvalidOperationException: Call UseKestrelHttpsConfiguration() on IWebHostBuilder
to automatically enable HTTPS when an https:// address is used.
```

The fix is the address, not that call: adding HTTPS back would stop the sample demonstrating the
slim builder, and a deployment terminates TLS at the reverse proxy anyway. The default
`launchSettings.json` the SDK writes for a new project *does* specify an `https://` URL, so the
one in `apps/sample/Properties/` is checked in to override it.
