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
dotnet build
dotnet test tests/__product__.tests.unit
dotnet test tests/__product__.tests.integration
dotnet test tests/__product__.tests.e2e
```

Integration tests need a real PostgreSQL instance; e2e publishes the app and drives it with
Playwright. All three tiers pass before anything merges.

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

## The package-feed gap

Package mode (`-p:NativeWebUsePackages=true -p:JauntyQUsePackages=true`) is what CI is wired
for, but **the private NativeWeb/JauntyQ feed does not exist yet**, so a package-mode restore
cannot succeed anywhere — including `.github/workflows/ci.yml`, which dies at Restore until a
feed is provisioned. Checkout mode is the working path today. The packages are private-feed
only in any case: the JauntyQ license limits generated output to self-hosted use.

## Before shipping

The stamp leaves deliberate placeholders. Replace every one before the product faces traffic:

- `wwwroot/robots.txt` and `wwwroot/sitemap.xml` — `__product__.example` host.
- `SecurityTxtEndpoint.cs` — the `security@__product__.example` contact.
- `wwwroot/img/logo.svg` — the placeholder logo.
- `deploy/__product__.service` / `install-windows-service.ps1` — connection string and URLs.
- `docs/security-baseline.md` — tick the checklist, honestly.
- When the package feed exists: add a `NuGet.config` with the private source and a
  `packageSourceMapping` confining `NativeWeb.*`/`JauntyQ.*` to it, so package mode never
  resolves those IDs against nuget.org.

## Status

See `work/status/current.md` for what is being worked on right now, and `CHANGELOG.md` for
what has shipped.
