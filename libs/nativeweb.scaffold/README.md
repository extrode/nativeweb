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
    [ScaffoldList][ScaffoldSearch]       public string Title { get; set; } = "";
    [ScaffoldMultiline]                  public string Body { get; set; } = "";
    [ScaffoldList]                       public bool Published { get; set; }
    [ScaffoldList][ScaffoldReadOnly]     public DateTime CreatedAt { get; set; }
}
```

Implement `IScaffoldStore<Article>` (six methods, no query language — JauntyQ owns data access),
register it, and call `app.MapScaffolding()`. That is the entire wiring. See `apps/sample`.

Routes emitted per model, under `/admin/{route}`:

| Method | Path | Operation | Role from |
| --- | --- | --- | --- |
| GET | `/` | list, search, page | `Read` → `Role` |
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
