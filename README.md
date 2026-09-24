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
| The JauntyQ packages | public on nuget.org, no credential | `Extrode.JauntyQ.*` 0.5.1 |

**JauntyQ is load-bearing and needs no checkout.** All data access goes through its source
generator, so nothing here builds without it, and its generated output is licensed for
self-hosted-SaaS use only (`AGENTS.md`). Since 2026-09-05 it resolves **from packages by default**:
`dotnet build` at the root or on any project just works, and no sibling `jauntyq/` is expected or
probed. Decision `docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md` records why the
default moved — JauntyQ split its generator and runtime out to a public core repository, and the
sibling checkout stopped carrying either project this repository referenced.

Two things follow, each verified rather than assumed:

- **Restore needs no credential.** JauntyQ core 0.5.0 (2026-09-17) renamed every package id and
  the generated namespace to `Extrode.JauntyQ.*` / `Extrode.JauntyQ.Generated`, and publishes them
  on nuget.org. The old `JauntyQ.*` ids were deleted from the `jauntyq-github` feed; a machine
  whose NuGet cache still holds them will keep building until the cache is cleared, then fail
  with `NU1101`.
- **Checkout mode is still there, behind `-p:JauntyQUsePackages=false`.** It expects `jauntyq`
  (the public core, `github.com/extrode/jauntyq`) as a sibling; `Directory.Build.props` probes
  `..\jauntyq` and `..\..\..\jauntyq`, the second being the depth a `.worktrees/<branch>` or
  `.claude/worktrees/<name>` checkout sits at, and neither names an ancestor directory.
  `-p:JauntyQRoot=<path>` overrides the location. Verified building against core 0.5.1 on
  2026-09-24.

## Run it

`apps/sample` needs no database to start: a missing connection string selects an in-memory store
rather than failing, which makes this the fastest way to see the thing work.

```
dotnet run --project apps/sample                          # http://localhost:5399/
dotnet watch run --project apps/sample --no-hot-reload    # the edit-and-see-it loop
```

`--no-hot-reload` is required, not a preference: under the default hot-reload session this project
detects no change at all. `getting-started.md` has the measurement and the matching csproj `Watch`
items that templates need.

It is the framework's reference application and the only deployable project here. `apps/web`, an
identity provider, used to sit beside it and left on 2026-08-31 —
[decision 007](docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md) says why, and epass.io owns
epass now.

## Build and test

```
dotnet build                                    # the whole solution
dotnet build apps/sample                        # one project
dotnet test --project tests/nativeweb.render.tests.unit   # one tier; the full list is in getting-started
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
| `docs/` | Specs, plans, architecture, decisions, conventions, lessons, [`configuration.md`](docs/02-reference/configuration.md) and [`breaking-changes.md`](docs/02-reference/breaking-changes.md) |
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

A stamped product consumes the framework libs dual-mode (spec 003 R1): sibling checkout via
`$(NativeWebRoot)` for co-development, or the `NativeWeb.*` packages with
`-p:NativeWebUsePackages=true`. `dotnet pack` on `libs/nativeweb.*` produces the packages —
**private feed only**; the JauntyQ license limits generated output to self-hosted use, and no
`NativeWeb.*` package has published yet (known gap, see the stamped README), so checkout mode
is the working path for the framework. Its JauntyQ half went the other way on 2026-09-05 and
resolves from packages by default, needing no checkout at all.

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
