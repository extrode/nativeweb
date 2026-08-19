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

## What the host still owns

`UseAntiforgery()` validates but does not reject: it records the failure on
`IAntiforgeryValidationFeature` and calls next, and the form reader then throws a bare
`InvalidOperationException` — a 500 where a 400 belongs. The host must run a middleware that turns
that feature into a 400; see `apps/web/Middleware/AntiforgeryValidationMiddleware.cs`. Moving it
into the framework is on `work/todo.md`.

Rate limiting is also the host's today. The scaffolded routes carry no policy.
