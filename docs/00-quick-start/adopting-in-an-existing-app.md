# Adopting NativeWeb in an existing application

[`getting-started.md`](getting-started.md) is the greenfield path: clone, build, run
`tools/new-product.ps1`, get a stamped product. This is the other one. You have an ASP.NET Core
application already, with its own solution, its own `Directory.Build.props` and its own
deployment, and you want the framework underneath it.

Read this if you have written a page shell, a CSP nonce, a clean-URL rewrite, flash messages or
content-hashed asset URLs by hand. All five ship in `libs/nativeweb.web`.

## 1. Reference the framework

**Checkout mode is the supported way to consume NativeWeb.** Clone this repository as a
**sibling** of your product:

```text
your-org/
  nativeweb/          <- this repository
  your-product/       <- yours
```

No `jauntyq/` sibling is needed. NativeWeb resolves JauntyQ from the pinned `Extrode.JauntyQ.*`
packages on nuget.org, which restore without a credential. Build with `-p:JauntyQUsePackages=false`
only if you want a sibling `jauntyq` checkout instead. The license terms still apply to what you
ship (see the constraints at the end of this page).

Then add to your product's `Directory.Build.props`. Both casings are probed: the repository is
`nativeweb` on disk, and [`getting-started.md`](getting-started.md) tells people to clone it as
`NativeWeb`.

```xml
<PropertyGroup>
  <NativeWebRoot Condition="'$(NativeWebRoot)' == '' and Exists('$(MSBuildThisFileDirectory)..\nativeweb')">$(MSBuildThisFileDirectory)..\nativeweb</NativeWebRoot>
  <NativeWebRoot Condition="'$(NativeWebRoot)' == '' and Exists('$(MSBuildThisFileDirectory)..\NativeWeb')">$(MSBuildThisFileDirectory)..\NativeWeb</NativeWebRoot>
  <NativeWebRoot Condition="'$(NativeWebRoot)' == ''">$(MSBuildThisFileDirectory)..\nativeweb</NativeWebRoot>
</PropertyGroup>

<Target Name="EnsureNativeWebRoot" BeforeTargets="BeforeBuild"
        Condition="!Exists('$(NativeWebRoot)\libs\nativeweb.render\nativeweb.render.csproj')">
  <Error Text="NativeWeb checkout not found at '$(NativeWebRoot)'. Pass -p:NativeWebRoot=&lt;path&gt;." />
</Target>

<Target Name="ReportFrameworkResolution" BeforeTargets="BeforeBuild">
  <Message Importance="high" Text="NativeWeb: checkout mode ($(NativeWebRoot))" />
</Target>
```

The guard matters. An unresolved root drops the project references with nothing worse than
warning `MSB9008`, so the build carries on and fails later as a pile of `CS0246`s pointing at
your own code. Without the guard, the error you read is never the error you have.
`skeleton/Directory.Build.props` carries the full version of this block, including package mode,
and is the file to copy from.

**Probe relatively, and name no parent directory.** A hard-coded ancestor breaks every build the
day that directory is renamed.

**If your product builds from a git worktree**, add the deeper probe too — a worktree sits three
directories down, so a single sibling probe resolves to nothing and every restore in every
worktree fails:

```xml
<NativeWebRoot Condition="'$(NativeWebRoot)' == '' and Exists('$(MSBuildThisFileDirectory)..\..\..\nativeweb')">$(MSBuildThisFileDirectory)..\..\..\nativeweb</NativeWebRoot>
```

### Why not packages

The thirteen packable projects under `libs/` carry `IsPackable` and a `PackageId`, and
`skeleton/Directory.Build.props` already has a `-p:NativeWebUsePackages=true` mode. **Do not use
it yet.** Package mode is approved and not working: it has never restored end to end, and the
repository has no release process for a consumed library — no stated semver guarantee, no
support window, no deprecation path. See
[`../decisions/2026-08-30-004-package-mode-for-consumers.md`](../decisions/2026-08-30-004-package-mode-for-consumers.md)
for what it obliges. Until that lands, a checkout is the supported model and you upgrade by
pulling.

### The project references

Add the ones you need. `nativeweb.render` and its generator are the minimum for rendering:

```xml
<ItemGroup>
  <ProjectReference Include="$(NativeWebRoot)\libs\nativeweb.render\nativeweb.render.csproj" />
  <ProjectReference Include="$(NativeWebRoot)\libs\nativeweb.render.generator\nativeweb.render.generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <ProjectReference Include="$(NativeWebRoot)\libs\nativeweb.web\nativeweb.web.csproj" />
</ItemGroup>
```

A generator is referenced as an **analyzer**, with `ReferenceOutputAssembly="false"`. Reference
it as an ordinary project and it compiles into your output and generates nothing.

**Import the assets targets by hand.** A `PackageReference` would pick this up on its own; a
`ProjectReference` does not, so nothing contributes the framework's browser assets (`boot.js` and
the design-system stylesheets under `/_nativeweb/`) or its shared templates to your product, and
every asset 404s at run time with no build error:

```xml
<Import Project="$(NativeWebRoot)\libs\nativeweb.web\build\NativeWeb.Web.targets" />
```

### Declare your templates

The render generator never reads disk — it sees only what the compiler hands it, so your
templates and locale catalogs have to be `AdditionalFiles`. Nothing warns when this is missing:
the generator emits no classes and your first reference to one is a `CS0103`.

```xml
<ItemGroup>
  <AdditionalFiles Include="templates\**\*.html" />
  <AdditionalFiles Include="locales\*.json" />
</ItemGroup>
```

`locales/en.json` is the root catalog and every `{{t:...}}` key must exist in it, so a product
that ships one language still ships that file.

## 2. Wire the pipeline

Two calls, in your composition root:

```csharp
WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

NativeWebOptions options = NativeWebOptions.FromConfiguration(builder.Configuration, "MyProduct");
builder.AddNativeWebWeb(options, typeof(Program).Assembly);

WebApplication app = builder.Build();
app.UseNativeWebPipeline(Locales.All);
app.MapNativeWebEndpoints(options, typeof(Program).Assembly);

// your own routes here

app.Run();
```

The two calls take different things and the difference is not a slip. `UseNativeWebPipeline`
takes the locale ids you ship — `Locales.All`, generated from your own `locales/*.json` — plus an
optional `Action<IApplicationBuilder>` for authentication, which must be registered there rather
than afterwards or every authenticated form post is a 400. `MapNativeWebEndpoints` is the one
that takes the options, because it reads the security contact and the readiness window from them.

`"MyProduct"` is both the application name and the configuration section, so every setting reads
from `MyProduct:`. Pass a third argument to `FromConfiguration` when they differ. Every key is in
[`../02-reference/configuration.md`](../02-reference/configuration.md).

Pass `typeof(Program).Assembly` rather than letting it default to the entry assembly: under a
`WebApplicationFactory` test the entry assembly is the test assembly, not yours, so `/version`
reports its version instead.

`AddNativeWebWeb` is a series of registrations behind one call, and every one of them is a public
method on `NativeWebPipeline`. If you need to sit between two of them, call them yourself.

### Two things that stop the process if you skip them

**`wwwroot/pages/error.html` is required.** The error shell is loaded once at startup, from a
static file, so that an error page never depends on the renderer or the database — either of
which may be the thing that just failed. It is your file, not the framework's: copy
[`apps/sample/wwwroot/pages/error.html`](../../apps/sample/wwwroot/pages/error.html) and restyle
it, keeping the `{{title}}` and `{{message}}` placeholders, which are literal-replaced. Without
it the host prints `wwwroot/pages/error.html is missing` and exits 78 — a refused configuration,
not a crash.

**Generated code lands in your `RootNamespace`.** A template at `templates/hello.html` in a
project rooted at `your_product` becomes `your_product.Templates.Hello`. Declare your own code in
that namespace, as `apps/sample` does. A `using your_product;` in a file outside it is not
enough: a using directive imports a namespace's types and not its nested namespaces, so
`Templates.Hello` still fails to resolve while `Locales` beside it resolves fine.

## 3. Delete what the framework now owns

| If you have | Delete it and use |
|---|---|
| A layout shell gathering nonce, antiforgery token and locale | `Chrome.For(context, assets, antiforgery, locale)` |
| CSP with a per-request nonce, HSTS, permissions policy | `SecurityHeadersMiddleware`, wired by `UseNativeWebPipeline` |
| A clean-URL rewrite and its startup route map | `StaticPagesMiddleware` |
| A one-shot flash cookie | `Flash.Set` / `Flash.Take` |
| Content-hashed asset URLs | `AssetUrls` |
| A hand-rolled `/healthz`, `/readyz`, `/version` | `MapNativeWebEndpoints` |
| Passphrase hashing, opaque tokens, PII masking | `libs/nativeweb.security` |
| A hand-written OAuth authorization-code flow | `libs/nativeweb.signin` |

Delete one at a time and run your tests between. The framework's versions differ in behaviour at
the edges — `Chrome.Anonymous` deliberately empties every per-request field so a public page can
be output-cached, which a hand-rolled shell usually does not do.

## 4. What you must keep

- **Your own routes, data access and domain.** NativeWeb has no ORM. Data access is
  JauntyQ, source-generated over PostgreSQL, and it is a separate adoption
  decision from this one.
- **Your `wwwroot`.** Framework assets arrive through an MSBuild `.targets` that contributes
  them to your static web assets; you do not copy them. Third-party files (HTMX, Alpine.js, fonts)
  are yours to vendor, as `apps/sample/wwwroot/vendor/` does.
- **NativeAOT discipline, if you publish AOT.** The framework builds with
  `TreatWarningsAsErrors` and `IsAotCompatible`. A reference of yours that is not
  trim-compatible fails your publish, not ours.

## 5. Constraints you inherit

- **PostgreSQL only.** No provider abstraction exists to swap.
- **Minimal APIs only.** No MVC, no Controllers, no Razor Pages, no Blazor.
- **HTMX and Alpine are vendored**, never from a CDN.
- **JauntyQ's source and packages are under ISL-R 1.2.** Code its generator emits is yours under
  its Output Exception, and `Extrode.JauntyQ.Runtime` may ship inside your built output under its
  Redistribution Exception. Nothing else of JauntyQ's may be redistributed.

## 6. Check it worked

```bash
dotnet build
```

The `ReportFrameworkResolution` target above prints `NativeWeb: checkout mode (<path>)`, so a
wrong root is visible in the log rather than at the first `CS0246`. Then run your app:

| Request | Expected |
|---|---|
| `/healthz` | 200 once the pipeline is mounted |
| `/readyz` | 503 until a connection string is configured — the answer that gates load-balancer traffic, and the right one before you have one |
| `/version` | 200, reporting the assembly you passed to `MapNativeWebEndpoints` |
| your own template route | 200, with the CSP, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and `Permissions-Policy` headers already set |

Those four are what this guide was checked against: a stock `dotnet new web` project taken
through every step above, serving a generated template page.

**Before you deploy behind a proxy, set `{Section}:ForwardedHeaders:KnownProxies`.** This is the
one step whose omission the table above cannot see: without it the app builds, boots, serves
every page and is wrong about who is calling. `RemoteIpAddress` is the proxy's address rather
than the client's, so every request lands in one rate-limit partition and every access log names
the same host. Nothing fails and nothing warns. Set it to the comma-separated addresses of the
hops in front of you, and confirm by logging `HttpContext.Connection.RemoteIpAddress` on one
request from outside. This repository's own `deploy.sh` refuses to promote a release without it;
your deployment has no such guard unless you write one.

To assert them rather than eyeball them, reference `Microsoft.AspNetCore.Mvc.Testing` from a test
project and drive the four through a `WebApplicationFactory<Program>`. Top-level statements make
`Program` internal, so add `public partial class Program;` to the end of the file or the factory
will not close over it — and this is where passing `typeof(Program).Assembly` earns its keep,
because the entry assembly under a factory is the test assembly, not the product's.

## If you get stuck

- [`../../libs/nativeweb.web/README.md`](../../libs/nativeweb.web/README.md) — the pipeline in
  detail, including what each registration does.
- [`../02-reference/configuration.md`](../02-reference/configuration.md) — every setting and its
  failure mode. Three of them stop the process at startup on purpose.
- [`../02-reference/breaking-changes.md`](../02-reference/breaking-changes.md) — what changed
  between the version you cloned and the one you pulled.
