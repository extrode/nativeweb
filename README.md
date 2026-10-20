# NativeWeb

A server-first web framework for ASP.NET Core. Pages are rendered as HTML on the server, a
little htmx makes them feel quick, and the whole app compiles to one self-contained native
binary.

[![CI](https://github.com/extrode/nativeweb/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/extrode/nativeweb/actions/workflows/ci.yml)
[![License: ISL-NETC](https://img.shields.io/badge/license-ISL--NETC%201.2-blue)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](#requirements)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-tested%20end%20to%20end-brightgreen)](#how-it-is-built)
[![Database](https://img.shields.io/badge/database-PostgreSQL-336791)](#how-it-is-built)

> [!IMPORTANT]
> NativeWeb is source-available, not open source. The [ISL-NETC 1.2](LICENSE.md) license is a
> network copyleft: if you run a *modified* copy as a service, you must offer its users the source
> of that exact version. It also carries ethical-use terms that bind everyone downstream. Read
> [License](#license) before you build on it.

If you have built an ASP.NET Core app with Minimal APIs, you already know most of NativeWeb. A
page is an HTML template:

```html
<!--#template
  @layout layout/page
  @param IReadOnlyList<PublishedArticle> Articles
-->
<h1>{{t:home.title}}</h1>
{{> articles/results Articles=Articles}}
```

and a plain `MapGet` that fills it in:

```csharp
endpoints.MapGet("/", async (IPublishedArticles articles, HttpContext http) =>
{
    IReadOnlyList<PublishedArticle> recent = await articles.RecentAsync(20, http.RequestAborted);
    return Templates.Site.Home.Result(locale: Locales.Get(http.GetLocale()), Articles: recent);
});
```

Both are trimmed from the home page in [`apps/sample`](apps/sample/PublicSite.cs). The template is
compiled into C# at build time, so `Templates.Site.Home.Result` is a real method with typed
parameters. The data comes from a `.sql` file that [JauntyQ](https://github.com/extrode/jauntyq)
also turns into a typed method at build time. There is
no SPA, no JavaScript build step, no MVC and no Razor. When something is wrong (a missing column, a
template referring to a field that doesn't exist), you find out when you build, not when a user
opens the page.

---

## Contents

- [Quick start](#quick-start)
- [What's in the box](#whats-in-the-box)
- [Admin screens for free](#admin-screens-for-free)
- [How it is built](#how-it-is-built)
- [Start your own app](#start-your-own-app)
- [Build and test](#build-and-test)
- [Requirements](#requirements)
- [Repository layout](#repository-layout)
- [Documentation](#documentation)
- [License](#license)
- [Contributing](#contributing)

---

## Quick start

You don't need a database to try it. With no connection string configured, the sample app runs
on an in-memory store.

```
git clone https://github.com/extrode/nativeweb.git
cd nativeweb
dotnet run --project apps/sample
```

Then open <http://localhost:5399/>. What you can look at:

- `/` and `/articles`: published articles. The filter on `/articles` posts over htmx and swaps
  only the list. The same URL also answers as JSON and CSV.
- `/articles/{slug}` and `/tags`: article and tag pages.
- `/admin/articles`: a full create/edit/delete admin, generated from attributes on one class.
  It sits behind a demo sign-in.

To edit and see changes without restarting:

```
dotnet watch run --project apps/sample --no-hot-reload
```

Keep `--no-hot-reload`. Without it the watcher misses template changes, and the app keeps serving
the old markup.

To run against PostgreSQL, set `ConnectionStrings:Sample` and the same code runs through JauntyQ
instead of the in-memory store. [`db/sample/README.md`](db/sample/README.md) has the setup.

---

## What's in the box

Take the libraries you need. Most stand alone; `web`, `signin` and `permissions` pull in the
one or two others they build on. Several come in two halves: a runtime library and a source
generator that writes the boring code at build time.

| Library | What it gives you |
|---|---|
| [`nativeweb.web`](libs/nativeweb.web/README.md) | The pipeline in front of your routes: security headers, antiforgery, rate limiting, output caching, forwarded headers, telemetry, flash messages, cache-busting asset URLs |
| [`nativeweb.render`](libs/nativeweb.render/README.md) | HTML templates and locale files compiled into C#, written straight to the response with no whole-page string |
| [`nativeweb.forms`](libs/nativeweb.forms/README.md) | Mark a record `[Form]` and get a generated binder and a POST route with antiforgery and bot checks already attached |
| [`nativeweb.scaffold`](libs/nativeweb.scaffold/README.md) | Admin CRUD screens generated from attributes on a model |
| [`nativeweb.permissions`](libs/nativeweb.permissions/README.md) | Per-model, per-operation permissions with groups, enforced by the generated admin |
| [`nativeweb.signin`](libs/nativeweb.signin/README.md) | Sign-in through any OAuth 2.1 / OpenID Connect provider (PKCE, server-side sessions, refresh, sign-out) |
| [`nativeweb.security`](libs/nativeweb.security/README.md) | Password hashing (Argon2id), opaque tokens, encryption at rest, blind indexes, masking, bot traps |
| [`nativeweb.mail`](libs/nativeweb.mail/README.md) | Outbound e-mail over SMTP with recipient validation and header-injection protection |

NativeWeb is a framework, not a product. It signs users in through an identity provider, but it
is not one: there is no token issuer, consent screen or user database here, and there won't be.

---

## Admin screens for free

Decorate a model:

```csharp
[Scaffold(Display = "Articles", Route = "articles")]
[Auth(Role = "admin", Delete = "owner")]
public sealed class Article
{
    [ScaffoldKey]                        public string Id { get; set; } = "";
    [ScaffoldList][ScaffoldSearch]
    [ScaffoldSort]                       public string Title { get; set; } = "";
    [ScaffoldMultiline]                  public string Body { get; set; } = "";
    [ScaffoldList]                       public ArticleStatus Status { get; set; }
    [ScaffoldList]                       public DateOnly? PublishedAt { get; set; }
}
```

implement `IScaffoldStore<Article>` (six methods), register it, and call `app.MapScaffolding()`.
You get a searchable, sortable, paged list, plus create, edit and delete forms under
`/admin/articles`, each checking the role you named.

Scaffolding is for the CRUD screens only. The public side of an app is hand-written, because which
rows to show, in what order, and how they look are product decisions. The
[tutorial](docs/00-quick-start/tutorial-first-feature.md) builds one such page end to end.

---

## How it is built

- **.NET 10, Minimal APIs, NativeAOT.** Every app publishes as one self-contained native binary.
  The end-to-end tests run against that binary, not the JIT build, so anything that breaks under
  trimming breaks a test. Warnings are errors across the repository.
- **Server-rendered HTML first.** Every page works without JavaScript. htmx is layered on top for
  partial updates, following the rules in
  [progressive enhancement](docs/01-architecture/progressive-enhancement.md). Alpine.js is vendored
  and loaded but not used by the sample yet.
- **JauntyQ for all data access.** Queries are real `.sql` files, checked against a committed schema
  snapshot when you build. There's no EF Core, no Dapper and no hand-written ADO.NET.
- **PostgreSQL** is the database the sample and the test suites run against.
- **No CDN, no npm.** htmx and Alpine.js are vendored into the repository. Our own JavaScript is a
  few small files served as-is, with no build step.

The reasoning behind each choice is in
[native-web-architecture.md](docs/01-architecture/native-web-architecture.md).

---

## Start your own app

`skeleton/` is a ready-made app shell: the request pipeline, templates and locales, static pages,
a database migration runner, three test projects, CI and deployment units. Stamp a copy with your
app's name:

```
pwsh tools/new-product.ps1 -Name Probe -Slug probe -Destination ../probe            # dry run
pwsh tools/new-product.ps1 -Name Probe -Slug probe -Destination ../probe -Execute
```

By default the stamped app references the NativeWeb libraries from a checkout of this repository
sitting next to it. It can switch to NuGet packages with `-p:NativeWebUsePackages=true`, but those
packages aren't published yet, so the checkout is the way for now. JauntyQ comes from nuget.org
and needs no checkout.

To check that `skeleton/` still builds before you stamp the app you mean to keep,
`deno run -A tools/stamp-check.ts` stamps a throwaway copy into `tmp/`, builds it and runs its
tests.

[Getting started](docs/00-quick-start/getting-started.md) walks through stamping and running your
first app. [Adopting in an existing app](docs/00-quick-start/adopting-in-an-existing-app.md) covers
bringing individual libraries into an app you already have.

---

## Build and test

```
dotnet build
dotnet test --project tests/nativeweb.render.tests.unit
```

Each library has up to three test tiers: unit (xUnit), integration (`WebApplicationFactory`
against a real PostgreSQL) and end-to-end (Playwright, against the NativeAOT binary). The
integration and end-to-end tiers need a test database and a Playwright browser install; both are
covered in [getting started](docs/00-quick-start/getting-started.md).

---

## Requirements

| | Version |
|---|---|
| .NET SDK | 10.0.100 or later (pinned in `global.json`) |
| PostgreSQL | any recent version; optional for the sample app |
| PowerShell | 7 or later, for the scripts in `tools/` and `scripts/` |

The [JauntyQ](https://github.com/extrode/jauntyq) packages restore from nuget.org with no
credentials. If you're working on JauntyQ itself, build against a local checkout with
`-p:JauntyQUsePackages=false`. It looks for a `jauntyq` folder next to this repository (or three
levels up, for a worktree), and `-p:JauntyQRoot=<path>` points it elsewhere.

---

## Repository layout

| Path | What's there |
|---|---|
| `apps/sample` | The reference app, and the only deployable project |
| `libs/` | The framework libraries and their source generators |
| `tests/` | Unit, integration and end-to-end tests, per library |
| `skeleton/` | The app shell `tools/new-product.ps1` stamps |
| `db/sample` | The sample app's schema, migrations and queries |
| `docs/` | Guides, reference, architecture, specs and decisions |
| `tools/`, `scripts/` | Developer tooling and operational scripts |
| `work/` | Current work: task list, status, backlog |

---

## Documentation

- [Getting started](docs/00-quick-start/getting-started.md): from a clean clone to a running app
  and a green test suite.
- [Tutorial: your first feature](docs/00-quick-start/tutorial-first-feature.md): a query, a
  template, a route and a test at every tier.
- [Architecture](docs/01-architecture/native-web-architecture.md): why the stack is what it is.
- [Configuration](docs/02-reference/configuration.md): every setting, its default, and what a
  wrong value costs.
- [Security baseline](docs/02-reference/security-baseline.md) and
  [template diagnostics](docs/02-reference/template-diagnostics.md).
- [`docs/README.md`](docs/README.md) indexes the rest.

What changed and when is in [CHANGELOG.md](CHANGELOG.md), and anything that will break an
existing app is in [breaking-changes.md](docs/02-reference/breaking-changes.md).

---

## License

Copyright (c) 2026 Extrode LLC. Licensed under the **Islamic Software License - Network Copyleft
(ISL-NETC), Version 1.2** (SPDX `LicenseRef-ISL-NETC-1.2`). The text is in [LICENSE.md](LICENSE.md),
also published at <https://islamiclicense.org/isl-netc/1.2/LICENSE.md>.

Two things reach further than a permissive license would:

- **Running a modified version as a network service** obliges you to offer its users the
  corresponding source of that exact version, free of charge, under the same license (§3.6). Not
  distributing copies doesn't get you out of it.
- **Ethical-use restrictions** (§§4 and 5) bind every recipient and every user downstream, and can't
  be relicensed away.

It is not an OSI-approved open-source license. [NOTICE.md](NOTICE.md) names the Licensor and must be
kept with any copy (§3.1(c)).

---

## Contributing

Bug reports, reproduction steps and design proposals are welcome as GitHub issues. Code
contributions aren't accepted yet, and AI-generated contributions won't be accepted at all; see
[CONTRIBUTING.md](CONTRIBUTING.md).

Please report security issues privately, not as public issues: see
[SECURITY.md](.github/SECURITY.md).
