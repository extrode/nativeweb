# nativeweb.permissions

Per-model, per-operation permissions with groups. Decorate a model; the generated admin enforces
four permissions it never made you name.

This project is the **runtime** half: the attributes, the `Permission` struct, the endpoint
filter, the per-request held set, the `IPermissionStore` seam and the access admin. The
**build-time** half is `libs/nativeweb.permissions.generator`, which emits one constant per
permission and the catalog. A consumer references both:

```xml
<ProjectReference Include="..\..\libs\nativeweb.permissions\nativeweb.permissions.csproj" />
<ProjectReference Include="..\..\libs\nativeweb.permissions.generator\nativeweb.permissions.generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## The whole API

```csharp
[assembly: PermissionScope("sample")]

[Scaffold(Display = "Articles", Route = "articles")]
[Auth(Role = "admin")]
[Permissions]
public sealed class Article { /* ... */ }
```

That is the declaration. It produces four names — `sample:article.view`, `.add`, `.change`,
`.delete` — reached as `ArticlePermissions.View`, `.Add`, `.Change`, `.Delete`, and collected in
`PermissionCatalog.All`.

| Type | Where | What it does |
| --- | --- | --- |
| `[PermissionScope("x")]` | assembly | The `{scope}` half of every name in this assembly. Two assemblies declaring `Article` produce different permissions |
| `[Permissions]` | class | The four. `[Permissions("publish")]` adds a fifth that gates nothing by itself |
| `Permission` | struct | One name. Every API takes this, never a string, which is what makes a removed declaration a build error |
| `IPermissions.AllowsAsync` | injected | The one call a hand-written endpoint makes |
| `IPermissionContext.HeldAsync` | injected | The subject's whole held set, one store read per subject |
| `PermissionEndpointFilter` | filter | What the scaffolding appends to each generated route, and what you add to gate your own |
| `IPermissionStore` | seam | Where grants live. You implement it, or take `InMemoryPermissionStore` |
| `MapAccessAdmin` | endpoints | Groups, members and grants, as pages |

## Wiring

```csharp
builder.Services.AddPermissions(PermissionCatalog.All);
builder.Services.AddSingleton<IPermissionStore>(sp => new MyPermissionStore(/* ... */));

app.MapScaffolding();
app.MapAccessAdmin("/admin", ScaffoldLayout.Page, ScaffoldLayout.AntiforgeryField)
    .RequireAuthorization(policy => policy.RequireRole("admin"));
```

The catalog is passed rather than discovered: an assembly scan would make the set of declared
permissions depend on what happened to be loaded.

`MapAccessAdmin` takes the page shell and the antiforgery field as delegates, and returns the
group ungated. It cannot name `ScaffoldLayout`, which is generated into *your* assembly, and it
does not choose its own gate because the gate is the host's decision.

**Your pipeline must call `app.UseAntiforgery()`.** Each of the seven posts carries
`IAntiforgeryMetadata`, so the middleware validates the token the field delegate renders, and
routing refuses the request outright if that middleware is absent, rather than letting an
unvalidated post through. `UseNativeWebPipeline` already does this; a host wiring its
own pipeline calls it after `UseAuthentication` and `UseAuthorization`.

## A hand-written endpoint

A model needs no generated admin for any of this to work. Given
`[Permissions("publish")]` on `Article`:

```csharp
app.MapPost("/articles/{id}/publish", async (string id, IPermissions permissions, HttpContext http) =>
    await permissions.AllowsAsync(http.User, ArticlePermissions.Publish, id)
        ? Results.Ok()
        : Results.Forbid());
```

Or the same gate as metadata, which is what the scaffolding does:

```csharp
app.MapPost("/articles/{id}/publish", Publish)
    .AddEndpointFilter(new PermissionEndpointFilter(ArticlePermissions.Publish, "id"));
```

## What the store owes you

`IPermissionStore` is fourteen methods over five tables. `apps/sample/JauntyPermissionStore.cs` is
a worked implementation over PostgreSQL through JauntyQ, and
`docs/specs/024-model-permissions/024-schema.sql` is the DDL it expects, kept as a reference
nothing applies.

**The schema is yours, not the framework's.** NativeWeb ships no migration runner and creates no
table. An application adopting this layer stamps the five tables and writes the store.

### You must seed the first operator

A fresh database has nobody holding a permission, so nobody can reach the access admin to make
the first grant, so the admin is unreachable forever. Every application on this layer seeds its
own first subject; `db/sample/migrations/0006_permissions.sql` is the sample's, and the subject
it names is a value you look up from your provider, not a name you can guess.

## What it deliberately does not do

- **No identity.** The subject is the `NameIdentifier` claim, whatever issued it. No credential,
  no profile field, no user table. epass.io owns identity; this reads a string. Nothing here
  depends on `nativeweb.signin`.

  **That claim is not configurable yet, and the failure is silent.** A scheme that surfaces the
  subject under any other name — a JWT bearer scheme configured not to map inbound claims is the
  ordinary case — yields no subject, which is the empty held set, which is a 403 from every gated
  route with nothing recorded to say why. Spec 025 is the fix; until it lands, a host on such a
  scheme maps the claim to NameIdentifier itself, through ASP.NET Core's claims transformation or
  its scheme's own claim actions.
- **No row-level decisions.** `AllowsAsync` and the filter both accept a resource identity and
  carry it to the store, and neither shipped store consults it. The decision is per model. That
  is what makes the held set one call per request instead of one per row — and the visible
  consequence is that a store which later grants per row would draw an Edit control on a row the
  subject cannot change, and refuse on the click.
- **No policy language, no relationship graph, no decision service.**
- **It does not replace `[Auth]`.** `[Auth]` still takes roles and policies only. A model
  carrying both has the role decided first, by the authorization middleware, which runs ahead of
  any endpoint filter — so the AND in "role *and* permission" is the ordering, not code.

## Removing a declaration

Two halves, and both matter.

**Compile:** every use site holds a `Permission`, and the only supply of values is the generated
constants. Delete the declaration and the call site fails as CS0117.

**Grant:** a grant row naming a permission the catalog no longer declares is inert — the held set
is intersected with the registry, so it can never admit anyone. The access admin still lists it,
marked `undeclared`, offering a revoke control and nothing else. Hiding it would leave a row
nobody could find to delete; offering it in the add list would let it be re-granted.

## Which suite proves it

| Tier | Project |
| --- | --- |
| Unit | `tests/nativeweb.permissions.tests.unit` — the registry, the filter, the context, the access admin over a TestServer |
| Unit (generator) | `tests/nativeweb.scaffold.tests.unit/PermissionGatingTests.cs` — the emitted source, compiled and asserted |
| Integration | `tests/nativeweb.permissions.tests.integration` — `JauntyPermissionStore` against real PostgreSQL |
| Integration (admin) | `tests/nativeweb.scaffold.tests.integration/PermissionGatingTests.cs` — the rendered controls, with and without each grant |
| End-to-end | `tests/nativeweb.permissions.tests.e2e` — a browser against the published NativeAOT sample |
| AOT | `tests/nativeweb.signin.tests.aot` — the publish raises no trim or AOT warning |
