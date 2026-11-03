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

```bash
bash db/migrate.sh
dotnet run --project apps/__product__
```

The connection string lives in user-secrets locally
(`__PRODUCT__:Db:ConnectionString`), not in `appsettings.json`.

**Both frameworks come from packages.** The build restores the pinned `Extrode.NativeWeb.*` and
`Extrode.JauntyQ.*` versions in `Directory.Packages.props` from nuget.org, with no credential and
no sibling checkout. To build against a NativeWeb checkout instead, for a framework change that
is not released yet, opt in; `../NativeWeb` is probed, or pass the path:

```bash
dotnet run --project apps/__product__ -p:NativeWebUsePackages=false -p:NativeWebRoot=<path>
```

## Build and test

```bash
export __PRODUCT____Db__ConnectionString="Host=127.0.0.1;Database=__product___dev;Username=__product___app;Password=..."
dotnet build
dotnet test --project tests/__product__.tests.unit
dotnet test --project tests/__product__.tests.integration
dotnet test --project tests/__product__.tests.e2e
```

Integration tests need a real PostgreSQL instance; e2e publishes the app and drives it with
Playwright. All three tiers pass before anything merges.

Two things about the last two tiers are not obvious:

- **The integration tier reads `__PRODUCT____Db__ConnectionString` from the environment.** Without
  it the tier still runs, but `/readyz` reports `ServiceUnavailable` and one fact fails. The
  double-underscore spelling is the environment form of `__PRODUCT__:Db:ConnectionString`.
- **In checkout mode, set the switch and the roots as environment variables, not `-p:` flags.**
  `-p:NativeWebUsePackages=false -p:NativeWebRoot=...` satisfies `dotnet build` and then fails the
  e2e tier, because that tier's fixture shells out to its own `dotnet publish` and command-line
  properties do not cross into a child process. Environment variables do:

  ```bash
  export NativeWebUsePackages=false
  export NativeWebRoot=/path/to/nativeweb
  ```

  The symptom is a publish failure inside the e2e fixture reading *"Skipping project … because it
  was not found"*, which names NativeWeb's libraries and looks nothing like a path problem.

## Publish

```bash
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

`.github/workflows/ci.yml` builds what a developer machine builds: NativeWeb and JauntyQ from the
pinned packages on nuget.org, with no framework checkout and no repository secret. A framework
push cannot turn this product red; only a change to the pins here can.

This product is checked out into `product/`, not the workspace root, and
`defaults.run.working-directory: product` keeps every `run:` step there. No clone sits beside it
today, but a product that adds one back, to build against an unreleased framework change, needs
that layout: `Directory.Packages.props` discovery walks up from each project and does not stop at
a repository boundary, so a clone placed *inside* `product/` would inherit this repo's copy
(NU1008). Check the framework out beside `product/` and set `NativeWebUsePackages: "false"` and
`NativeWebRoot` as workflow-level *environment* variables rather than `-p:` flags: `dotnet list
package` accepts no `-p:`, and the e2e fixture's NativeAOT publish runs as a child process.

**JauntyQ packages are its default.** Nothing needs to be passed: the pins are
`Extrode.JauntyQ.*`, public on nuget.org, and restore with no credential. Checkout mode, `-p:JauntyQUsePackages=false`, builds against a sibling
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
- `nuget.config` — nuget.org alone, with a `*` source mapping. Every pin, `Extrode.NativeWeb.*`
  and `Extrode.JauntyQ.*`, restores from it with no credential. Add a mapping for any source you
  add beside it.
- `.github/workflows/ci.yml` — the `extrode/nativeweb` checkout target and its `ref: main`. Pin
  that to a tag once the framework publishes one.

## Status

See `work/status/current.md` for what is being worked on right now, and `CHANGELOG.md` for
what has shipped.
