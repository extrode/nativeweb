# NativeWeb

NativeWeb is a reference implementation and reusable template for the **Lean Native Web**
architecture — ASP.NET Core Minimal APIs serving server-rendered HTML enhanced with HTMX and
Alpine.js, compiled to a single self-contained NativeAOT binary, with all data access flowing
through the JauntyQ source-generated layer over PostgreSQL — for developers who want a small,
fast, dependency-light web application without a SPA build pipeline.

**New here?** [`docs/00-quick-start/getting-started.md`](docs/00-quick-start/getting-started.md) is the ordered path from a
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
- **A per-project build probes for the same sibling, and no longer cares what the parent is
  called.** `Directory.Build.props` tries `..\jauntyq` and `..\..\..\jauntyq`, the second being the
  depth a `.worktrees/<branch>` or `.claude/worktrees/<name>` checkout sits at. Both probes now
  agree with `NativeWeb.slnx`, which has always named `../jauntyq/...` relatively. Until
  2026-08-26 they instead hard-coded an ancestor literally named `extrode-legacy-org.com`, so a renamed
  source root broke every per-project build while the solution build kept working — the fix, and
  the fix is in `Directory.Build.props`.
  `-p:JauntyQRoot=<path>` remains the override for a checkout that is not a sibling.
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

`apps/sample` needs no database to start: a missing connection string selects an in-memory store
rather than failing, which makes this the fastest way to see the thing work.

```
dotnet run --project apps/sample                # http://localhost:5399
```

It is the framework's reference application and the only deployable project here. `apps/web`, an
identity provider, used to sit beside it and left on 2026-08-31 —
[decision 007](docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md) says why, and epass.io owns
epass now.

## Build and test

```
dotnet build                                    # solution; needs the sibling jauntyq checkout
dotnet build apps/sample                        # one project; two-depth probe, no sibling needed
dotnet test tests/nativeweb.render.tests.unit   # one tier; the full list is in getting-started
```

Every command on this page was executed in the form written, from a fresh clone, on
2026-08-25 — Windows 11 (10.0.26200.8457), .NET SDK 10.0.203, PostgreSQL 18.2, PowerShell 7.6.4.

A read-only nightly audit runs on a separate host and reports defects for a human to triage; it
never writes to this repository. Provisioning, reading a digest and triaging are in
[`continuous-audit.md`](docs/02-reference/continuous-audit.md).

## Layout

| Path | What |
|---|---|
| `apps/` | Deployable services. `apps/sample` is the reference application |
| `libs/` | Framework libraries: render, forms, scaffold and their generators, security, mail, assets |
| `tests/` | Up to three tiers per library: `*.tests.unit`, `*.tests.integration`, `*.tests.e2e` |
| `work/` | Current work: tasklist, status window, backlog, milestones |
| `docs/` | Specs, plans, architecture, decisions, conventions, lessons, and [`configuration.md`](docs/02-reference/configuration.md) |
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

## License

**Islamic Software License - Network Copyleft (ISL-NETC), Version 1.1** —
SPDX `LicenseRef-ISL-NETC-1.1`. Copyright (c) 2026 Extrode LLC.

| file | what it is |
|---|---|
| [`LICENSE.md`](LICENSE.md) | the [ISL-NETC 1.1 text](https://islamiclicense.org/isl-netc/1.1/LICENSE.md), with §1.2 `[LICENSOR]` completed as Extrode LLC and unmodified otherwise |
| [`NOTICE.md`](NOTICE.md) | who the Licensor is, the two copyrights, and what binds an operator — **required to be retained by §3.1(c)** |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | ISL-CLA 1.1 adoption, including the **no-AI-contributions** rule |

Two consequences that reach further than a permissive license: **§3.6** obliges anyone running a
*modified* version as a network service to offer its users the corresponding source of that exact
version, at no charge, under the same license — never distributing a copy does not avoid it; and
**§§4 and 5** carry ethical use restrictions that bind every recipient and every user downstream
and cannot be relicensed away. This is a source-available license, not an open-source one, and it
is not OSI-approved. Read `LICENSE.md` before depending on this.

**Contributions are not accepted yet**, and AI-generated ones will not be at all — bug reports,
reproduction steps and written design proposals are the route. See `CONTRIBUTING.md`.

## Status

See `work/status/current.md` for what is being worked on right now, and `CHANGELOG.md` for
what has shipped.

---

**Local READMEs.** Any directory whose purpose is not obvious from its name gets its own
`README.md`, kept to a few lines: what belongs here, what does not, and the naming rule if
there is one. The test is simple — if someone would have to open three files to work out what
a directory is for, it needs one.
