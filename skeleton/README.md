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

**Checkout mode, for NativeWeb only.** The build resolves the NativeWeb framework libs from a
sibling checkout by default, so `../NativeWeb` must exist — or pass the path explicitly. JauntyQ
resolves from packages and needs no sibling.

```
dotnet run --project apps/__product__ -p:NativeWebRoot=<path>
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
  export NativeWebRoot=/path/to/nativeweb
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

`.github/workflows/ci.yml` resolves NativeWeb in **checkout mode**, the same mode a developer
machine uses, and JauntyQ from packages. A runner has no sibling checkout, so the workflow clones
`extrode/nativeweb` and points `NativeWebRoot` at it. That is a workflow-level *environment*
variable rather than a `-p:` flag, because `dotnet list package` accepts no `-p:` and the e2e
fixture's NativeAOT publish runs as a child process — MSBuild reads bare environment variables as
properties, so one definition covers all of it.

The workspace is laid out as **siblings**, with this product in `product/`:

```
$GITHUB_WORKSPACE/
  product/     <- this repository
  NativeWeb/
```

That is required, not tidiness. `Directory.Packages.props` discovery walks up from each project
and does not stop at a repository boundary, so a framework clone placed *inside* `product/`
inherits this repo's copy — which sets `ManagePackageVersionsCentrally=true` over a repository
that does not expect it. Measured 2026-08-17 against the jauntyq clone this workspace used to
carry: NU1008 on both JauntyQ projects, where the sibling layout built clean. `defaults.run.working-directory: product` is
what keeps every `run:` step pointed at this repository.

**Set the `FRAMEWORK_CHECKOUT_TOKEN` repository secret** before the first run. The NativeWeb repo
is private and the default `GITHUB_TOKEN` is scoped to this repository only, so without it the
checkout step fails with `Repository not found`.

**NativeWeb package mode (`-p:NativeWebUsePackages=true`) still exists and is still unusable:**
no `NativeWeb.*` package has published, so a package-mode restore of the framework cannot succeed
anywhere. CI used to force it and died at Restore on every run; it no longer depends on it.

**JauntyQ went the other way on 2026-09-05 and packages are its default.** Nothing needs to be
passed for the default: the pins are `Extrode.JauntyQ.*`, public on nuget.org since core 0.5.0, and
restore with no credential. Checkout mode, `-p:JauntyQUsePackages=false`, builds against a sibling
clone of the public core (`github.com/extrode/jauntyq`). The JauntyQ license limits generated
output to self-hosted use, which is also why enabling this workflow in a repository outside those
terms is not permitted.

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
- `nuget.config` — the `framework-github` source points at `extrode`'s registry. Repoint it if
  your framework packages publish elsewhere; the `packageSourceMapping` beside it is what keeps
  `NativeWeb.*`/`JauntyQ.*` from ever resolving against nuget.org, so keep the two in step.
- `.github/workflows/ci.yml` — the `extrode/nativeweb` checkout target and its `ref: dev`. Pin
  that to a tag once the framework publishes one.

## Status

See `work/status/current.md` for what is being worked on right now, and `CHANGELOG.md` for
what has shipped.
