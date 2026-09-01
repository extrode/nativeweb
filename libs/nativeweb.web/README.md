# nativeweb.web

The pipeline every NativeWeb application puts in front of its own routes: Kestrel limits,
forwarded headers, a pooled data source, telemetry, data protection, antiforgery, bot traps,
rate limiting, the public-page output cache, security headers, clean URLs, flash banners and
content-hashed asset URLs.

**If you have written any of the following by hand, this library replaces it.** That is not a
hypothetical: a consumer of this framework hand-ported five of them, each with a comment naming
the file it was copied from, and finished the day before this library completed. Nothing told
them it was coming.

| You wrote | Use instead |
|---|---|
| A page shell gathering nonce, antiforgery token and locale for the layout | `Chrome.For(context, assets, antiforgery, locale)` |
| CSP with a per-request nonce, HSTS, permissions policy, referrer policy | `SecurityHeadersMiddleware` — wired by `UseNativeWebPipeline` |
| A clean-URL rewrite mapping `/about` to `about.html`, with a route map built at startup | `StaticPagesMiddleware`, `StaticPagesMiddleware.BuildRouteMap(webRoot)` |
| A one-shot flash-message cookie read on the next render | `Flash.Set(context, kind, message)` / `Flash.Take(context)` |
| Content-hashed asset URLs so a stylesheet can be cached forever | `AssetUrls` |

## The whole API

Two calls do the work:

```csharp
WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

NativeWebOptions options = NativeWebOptions.FromConfiguration(builder.Configuration, "MyApp");
builder.AddNativeWebWeb(options, typeof(Program).Assembly);

WebApplication app = builder.Build();
app.UseNativeWebPipeline(options);
app.MapNativeWebEndpoints(options);

app.Run();
```

`AddNativeWebWeb` registers eleven things and hides none of them. Each is a public extension
method on `NativeWebPipeline`, so an application that needs to sit between two of them writes
the eleven calls out instead of the one:

| Call | What it registers |
|---|---|
| `AddNativeWebKestrel` | request body size and timeout limits |
| `AddNativeWebRequestTimeouts` | the per-request timeout policy |
| `AddNativeWebDataSource` | the pooled `NpgsqlDataSource` |
| `AddNativeWebForwardedHeaders` | `X-Forwarded-For`/`Proto` with the configured known proxies |
| `AddNativeWebResponseCompression` | Brotli for rendered HTML — configured, see below |
| `AddNativeWebTelemetry` | OpenTelemetry, opt-in and off by default |
| `AddNativeWebDataProtection` | the key ring, persisted to the configured directory |
| `AddNativeWebAntiforgery` | the antiforgery service and cookie |
| `AddNativeWebBotTraps` | the canary and timing trap |
| `AddNativeWebRateLimiting` | the `general` and `csp-report` policies |
| `AddNativeWebOutputCache` | the public-page cache policy |

Compression is **configured but not turned on**: `AddNativeWebWeb` registers it and
`UseNativeWebPipeline` does not call `app.UseResponseCompression()`. The paved deployment
compresses at Caddy instead, which measured 44% faster on the same page and offers zstd. An
application with no compressing proxy in front of it adds that one line and picks this
configuration up.

`AddNativeWebUserSecrets<TProgram>` is **not** part of `AddNativeWebWeb` and is called
separately. It loads user secrets in Development only, and inserts them *below* the environment
variables rather than above: appending is what `AddUserSecrets` does by default, and it makes
the store outrank a real environment variable. That was measured to bite — an integration
fixture wrote rows to the database an exported connection string named while the app under test
read the one in user secrets, and no failure said why.

### Endpoints it maps

`MapNativeWebEndpoints` mounts `/healthz`, `/readyz`, `/version`, the CSP report collector and
`/security.txt` — the last only when `{Section}:SecurityContact` is set.
`MapNativeWebReadiness` is separate so an application can mount readiness without the rest.

### Rate-limit policy names

`NativeWebPipeline.GeneralRateLimitPolicy` and `CspReportRateLimitPolicy` are the string
constants to pass to `RequireRateLimiting`; `PublicPagePolicy` is the output-cache policy name.
`ClientIpKey(context)` is the partition key those policies use, and is public because a product
adding its own policy should partition the same way.

## Configuration

One method reads it all: `NativeWebOptions.FromConfiguration(configuration, applicationName)`,
with an optional third argument when the configuration section is not the application name.
Every key, its default and its failure mode is in
[`docs/02-reference/configuration.md`](../../docs/02-reference/configuration.md), which a test
holds to the source.

Three settings stop the process rather than defaulting — telemetry, the data-protection key
directory and the security contact — because the silent version of each failure is invisible.
`StartupConfigGuards.FailStartup` is how: stderr, then exit `78`
(`StartupConfigGuards.ConfigurationErrorExitCode`). Never a modal dialog, which under NativeAOT
on Windows a supervisor reads as a hung service rather than an error.

## Cacheable public pages

`Chrome.Anonymous` exists for pages served from the output cache. Every field that would differ
between two otherwise-identical responses is empty — no antiforgery token (it would mint a fresh
masked token and set a cookie, and `OutputCache` refuses to store a response carrying
`Set-Cookie`), no nonce (the response is authorized by build-generated script hashes instead),
no bot canary (it signs the render instant, so it differs on every call).

A product that grows sign-in adds its identity fields to `Chrome` as **constants** in
`Anonymous`: that factory is reached only for a request carrying no session cookie, and passing
an identity through it would leak that user's name into a shared cache entry.

## What this library does not do

- **No routing, no views, no controllers.** Rendering is `nativeweb.render`; declarative CRUD is
  `nativeweb.scaffold`; form binding is `nativeweb.forms`.
- **No authentication.** `nativeweb.signin` is the relying party; the primitives it builds on
  are in `nativeweb.security`.
- **No `robots.txt` or `sitemap.xml`.** `/security.txt` is here because it is a fixed-format
  file with one configurable value. The other two need a canonical base URL and a route
  inventory the framework does not own.
- **`{Section}:ForwardedHeaders:ForwardLimit` is not bound.** `AddNativeWebForwardedHeaders`
  sets `ForwardedHeaders` and `KnownProxies` only, so the limit stays at the ASP.NET Core
  default of 1 whatever configuration says.

## Proved by

`tests/nativeweb.web.tests.unit` — the options binder, every startup guard, the security
headers, the static-page route map and its collision guard, and the readiness cache.
`apps/sample` exercises the whole pipeline through a real entry point, and
`tests/nativeweb.scaffold.tests.e2e` drives the published NativeAOT binary.
