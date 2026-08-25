# NativeWeb

NativeWeb is a reference implementation and reusable template for the **Lean Native Web**
architecture — ASP.NET Core Minimal APIs serving server-rendered HTML enhanced with HTMX and
Alpine.js, compiled to a single self-contained NativeAOT binary, with all data access flowing
through the JauntyQ source-generated layer over PostgreSQL — for developers who want a small,
fast, dependency-light web application without a SPA build pipeline.

**New here?** [`docs/getting-started.md`](docs/getting-started.md) is the ordered path from a
clean clone to a running application and a green suite. [`docs/README.md`](docs/README.md) indexes
everything else.

## Prerequisites

| | Required | Verified against |
|---|---|---|
| .NET SDK | `10.0.100` or later feature band (`global.json`) | 10.0.203 |
| PostgreSQL | any recent server; **optional to start**, see below | psql 18.2 |
| PowerShell | 7+, for `tools/` and `scripts/` | 7.6.4 |
| A JauntyQ checkout | **beside this one**, see the note | — |

**The JauntyQ prerequisite is load-bearing.** All data access goes through JauntyQ's source
generator, so nothing here builds without it, and it is used from a local checkout only and may not
be redistributed (`AGENTS.md`). Three things follow, each verified rather than assumed:

- **The solution build needs `jauntyq` as a literal sibling.** `NativeWeb.slnx` lists JauntyQ's two
  projects as solution members unconditionally, so anywhere else fails with `error MSB3202`. Note
  that `-p:JauntyQUsePackages=true` does **not** rescue it: that flag governs project references,
  not solution membership.
- **A per-project build is more forgiving.** `Directory.Build.props` probes two depths for the
  checkout, so `dotnet build apps/web` works from a nested location where the solution build will
  not. Pass `-p:JauntyQRoot=<path>` if your checkout lives elsewhere again.
- **With no checkout anywhere, the build stops with a clear message** rather than a wall of
  `CS0246`: *"JauntyQ checkout not found at '…'. Pass `-p:JauntyQRoot=<path>` or build with
  `-p:JauntyQUsePackages=true`."*

```
your-source-root/
  jauntyq/      <- the sibling the solution expects
  NativeWeb/    <- this repository
```

Package mode (`-p:JauntyQUsePackages=true`) is what CI uses, and it needs the `jauntyq-github`
source in `nuget.config` to be credentialed — see the comments in that file for the one-time
`gh auth token` setup. It appears to work with no credentials on a machine whose NuGet cache is
already populated from a previous `dotnet pack`, which is a trap worth knowing about: the cache is
standing in for the feed, and the same command fails on a clean machine.

## Run it

Neither application needs a database to start: a missing connection string selects an in-memory
store rather than failing, which makes this the fastest way to see the thing work.

```
dotnet run --project apps/sample                                  # http://localhost:5399
dotnet run --project apps/web --urls https://localhost:5444       # https, see below
```

`apps/web` must be served over **HTTPS**. Its session and antiforgery cookies are `Secure`-always,
so over plain HTTP every page carrying a form returns 500 (`/healthz` still answers 200, which
makes the failure easy to misread). The one launch profile in
`apps/web/Properties/launchSettings.json` is HTTP, so pass `--urls` as above. Run
`dotnet dev-certs https --trust` once if your browser objects to the certificate.

## Build and test

```
dotnet build                                    # solution; needs the sibling jauntyq checkout
dotnet build apps/web                           # one project; two-depth probe, no sibling needed
dotnet test tests/nativeweb.web.tests.unit      # one tier; the full list is in getting-started
```

Every command on this page was executed in the form written, from a fresh clone, on
2026-08-25 — Windows 11 (10.0.26200.8457), .NET SDK 10.0.203, PostgreSQL 18.2, PowerShell 7.6.4.

## Layout

| Path | What |
|---|---|
| `apps/` | Deployable services. `apps/web` is the epass.io IdP |
| `libs/` | Shared libraries: render, auth, data, mail, security, assets |
| `tests/` | Three tiers per app: `*.tests.unit`, `*.tests.integration`, `*.tests.e2e` |
| `work/` | Current work: tasklist, status window, backlog, milestones |
| `docs/` | Specs, plans, architecture, decisions, conventions, lessons, and [`configuration.md`](docs/configuration.md) |
| `scripts/` | Operational scripts. `scripts/cleanup/` is written but never run |

Agent instructions are in `AGENTS.md`. `CLAUDE.md` is a one-line shim that imports it.

## Stamping a new product

`skeleton/` is a tokenized product shell (spec 003 R2): app pipeline, templates/locales,
static pages, db migration runner, three test tiers, CI, deploy units, and the repo
operating files, with `__PRODUCT__`/`__product__` tokens where the product name goes. Its
`README.md` and `AGENTS.md` are the *stamped product's* files, not documentation of the
skeleton itself.

```
pwsh tools/new-product.ps1 -Name Manzil -Slug manzil -Destination C:\path\to\manzil.my           # dry run
pwsh tools/new-product.ps1 -Name Manzil -Slug manzil -Destination C:\path\to\manzil.my -Execute
```

A stamped product consumes the framework libs dual-mode (spec 003 R1), mirroring the JauntyQ
pattern: sibling checkout via `$(NativeWebRoot)` for co-development, or the `NativeWeb.*`
packages with `-p:NativeWebUsePackages=true`. `dotnet pack` on `libs/nativeweb.*` produces
the packages — **private feed only**; the JauntyQ license limits generated output to
self-hosted use, and no feed exists yet (known gap, see the stamped README), so checkout
mode is the working path today.

## Status

See `work/status/current.md` for what is being worked on right now, and `CHANGELOG.md` for
what has shipped.

---

**Local READMEs.** Any directory whose purpose is not obvious from its name gets its own
`README.md`, kept to a few lines: what belongs here, what does not, and the naming rule if
there is one. The test is simple — if someone would have to open three files to work out what
a directory is for, it needs one.
