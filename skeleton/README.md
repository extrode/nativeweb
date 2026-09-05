# __PRODUCT__

__PRODUCT__ — <one sentence: what this is and who it is for. If you cannot write it in one
sentence, the project does not know what it is yet.>

## Run it

Create the database and role once (superuser; details in `db/README.md`):

```sql
CREATE ROLE __product___app LOGIN PASSWORD '...';
CREATE DATABASE __product___dev OWNER __product___app;
```

Then apply the schema and start the app:

```
bash db/migrate.sh
dotnet run --project apps/__product__
```

The connection string lives in user-secrets locally
(`__PRODUCT__:Db:ConnectionString`), not in `appsettings.json`.

**Checkout mode.** The build resolves the NativeWeb framework libs and JauntyQ from sibling
checkouts by default, so `../NativeWeb` and `../../extrode-legacy-org.com/jauntyq` must exist — or pass
the paths explicitly:

```
dotnet run --project apps/__product__ -p:NativeWebRoot=<path> -p:JauntyQRoot=<path>
```

## Build and test

```
export __PRODUCT____Db__ConnectionString="Host=127.0.0.1;Database=__product___dev;Username=__product___app;Password=..."
dotnet build
dotnet test --project tests/__product__.tests.unit
dotnet test --project tests/__product__.tests.integration
dotnet test --project tests/__product__.tests.e2e
```

Integration tests need a real PostgreSQL instance; e2e publishes the app and drives it with
Playwright. All three tiers pass before anything merges.

Two things about the last two tiers are not obvious, and both were found by stamping a product and
running them rather than by reading anything:

- **The integration tier reads `__PRODUCT____Db__ConnectionString` from the environment.** Without
  it the tier still runs, but `/readyz` reports `ServiceUnavailable` and one fact fails. The
  double-underscore spelling is the environment form of `__PRODUCT__:Db:ConnectionString`.
- **If this product is not beside NativeWeb, set the roots as environment variables, not `-p:`
  flags.** `-p:NativeWebRoot=... -p:JauntyQRoot=...` satisfies `dotnet build` and then fails the
  e2e tier, because that tier's fixture shells out to its own `dotnet publish` and command-line
  properties do not cross into a child process. Environment variables do:

  ```
  export NativeWebRoot=/path/to/nativeweb JauntyQRoot=/path/to/jauntyq
  ```

  The symptom is a publish failure inside the e2e fixture reading *"Skipping project … because it
  was not found"*, which names NativeWeb's libraries and looks nothing like a path problem.

## Publish

```
dotnet publish apps/__product__ -c Release
```

NativeAOT: the output is a single self-contained binary plus `wwwroot/` and `appsettings*.json`.
`deploy/` has the systemd unit (`__product__.service`) and the Windows service registration
script (`install-windows-service.ps1`).

## Layout

| Path | What |
|---|---|
| `apps/` | Deployable services — `apps/__product__` is the web app |
| `libs/` | Shared libraries — `libs/__product__.data` hosts the JauntyQ generator |
| `tests/` | The three tiers: unit, integration, e2e |
| `db/` | Migrations, queries, schema snapshot, `migrate.sh` |
| `deploy/` | systemd unit and Windows service script |
| `work/` | Current work: tasklist, status window, backlog, milestones |
| `docs/` | Specs, plans, architecture, decisions, conventions, lessons |
| `scripts/` | Operational scripts. `scripts/cleanup/` is written but never run |

Agent instructions are in `AGENTS.md`. `CLAUDE.md` is a one-line shim that imports it.

## How CI resolves the framework

`.github/workflows/ci.yml` builds in **checkout mode**, the same mode a developer machine uses.
A runner has no sibling checkout, so the workflow clones `extrode-legacy-org/NativeWeb` and `extrode-legacy-org/jauntyq`
and points `NativeWebRoot` / `JauntyQRoot` at them. Both are workflow-level *environment*
variables rather than `-p:` flags, because `dotnet list package` accepts no `-p:` and the e2e
fixture's NativeAOT publish runs as a child process — MSBuild reads bare environment variables as
properties, so one definition covers all of it.

The workspace is laid out as **siblings**, with this product in `product/`:

```
$GITHUB_WORKSPACE/
  product/     <- this repository
  NativeWeb/
  jauntyq/
```

That is load-bearing, not tidiness. `Directory.Packages.props` discovery walks up from each
project and does not stop at a repository boundary, so a framework clone placed *inside*
`product/` inherits this repo's copy — which sets `ManagePackageVersionsCentrally=true`. jauntyq
ships no `Directory.Packages.props` and its `PackageReference`s carry `Version` attributes, so
central package management switches on underneath it and restore dies with NU1008 on both JauntyQ
projects (measured 2026-08-17; siblings build clean). `defaults.run.working-directory: product` is
what keeps every `run:` step pointed at this repository.

**Set the `FRAMEWORK_CHECKOUT_TOKEN` repository secret** before the first run. The framework repos
are private and the default `GITHUB_TOKEN` is scoped to this repository only, so without it the
checkout step fails with `Repository not found`.

Package mode (`-p:NativeWebUsePackages=true -p:JauntyQUsePackages=true`) still exists and is still
unusable: **the private NativeWeb/JauntyQ feed does not exist yet**, so a package-mode restore
cannot succeed anywhere. CI used to force it and died at Restore on every run; it no longer
depends on it. The packages are private-feed only in any case — the JauntyQ license limits
generated output to self-hosted use, which is also why enabling this workflow in a repository
outside those terms is not permitted.

## Before shipping

The stamp leaves deliberate placeholders. Replace every one before the product faces traffic:

- `wwwroot/robots.txt` and `wwwroot/sitemap.xml` — `__product__.example` host.
- **`__PRODUCT__:SecurityContact`** — where a vulnerability report should go. Unset, no
  `security.txt` is served at all, which is deliberate: a file naming a mailbox this product does
  not own sends the report nowhere and tells the reporter they are finished. Nothing warns when it
  is unset, so this is the item on the list with no symptom.
- `wwwroot/img/logo.svg` — the placeholder logo.
- `deploy/__product__.service` / `install-windows-service.ps1` — connection string and URLs.
- `docs/security-baseline.md` — tick the checklist, honestly.
- **The `FRAMEWORK_CHECKOUT_TOKEN` repository secret.** The one setup step with no artifact in
  this repo, so nothing here will remind you: CI cannot check out the framework without it.
- `nuget.config` — the `framework-github` source points at `extrode-legacy-org`'s registry. Repoint it if
  your framework packages publish elsewhere; the `packageSourceMapping` beside it is what keeps
  `NativeWeb.*`/`JauntyQ.*` from ever resolving against nuget.org, so keep the two in step.
- `.github/workflows/ci.yml` — the `extrode-legacy-org/NativeWeb` and `extrode-legacy-org/jauntyq` checkout targets,
  and the `ref: dev` on each. Pin those to a tag once the framework publishes them.

## Status

See `work/status/current.md` for what is being worked on right now, and `CHANGELOG.md` for
what has shipped.
