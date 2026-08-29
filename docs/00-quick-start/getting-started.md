# Getting started

From a clean clone to a running application and a green suite. Every command below was executed in
the form written before it was written down; the verification block at the end says on what.

**Two readers use this repository and they want different things.** Steps 1–4 are shared. After
that the guide forks once, explicitly, and you follow one path and ignore the other:

- **Path A — you want your own product.** NativeWeb is a template; you will stamp a new product out
  of `skeleton/` and work in that, not here.
- **Path B — you want to work on `apps/web`.** That is the epass.io identity provider, the
  reference implementation itself.

If you do not know which, read Path A first: most people arriving here want the template.

## Before you start

Prerequisites, including the JauntyQ checkout the build cannot do without, are in the
[README](../../README.md#prerequisites). Read that table now — the JauntyQ one is not optional and
not obvious, and it is the only thing here that can stop you before you begin.

## 1. Clone

```
git clone <this repository> NativeWeb
cd NativeWeb
```

Put it beside your `jauntyq` checkout, as the README's diagram shows. That adjacency is the only
layout requirement: the solution build needs it, and a per-project build probes for the same
sibling (plus the deeper spot a worktree checkout sits at). Neither cares what the source root is
called. If your JauntyQ checkout is somewhere else entirely, pass `-p:JauntyQRoot=<path>`.

**On the name:** the command above clones into `NativeWeb`, and the repository itself is
`nativeweb`. Both spellings work, and a stamped product probes for both, so nothing here depends
on which you pick. It only matters on a case-sensitive filesystem, and only for a *third* name —
if you clone under something else on Linux, set `NativeWebRoot` rather than relying on the probe.

## 2. Build

```
dotnet build
```

Roughly a minute cold. If it fails immediately with two `error MSB3202` lines naming
`../jauntyq/src/...`, your checkout is not beside `jauntyq` — that is the prerequisite above, and
`-p:JauntyQUsePackages=true` will **not** work around it. Build one project instead
(`dotnet build apps/web`) while you sort the layout out.

## 3. Run it, with no database at all

This is the checkpoint worth reaching first, because it removes PostgreSQL from the list of things
that can be wrong.

```
dotnet run --project apps/sample
```

Then open `http://localhost:5399/admin/articles`. **A missing connection string is not an error
here** — it selects an in-memory store, deliberately, so that the demo, the integration tier and
the browser tier all work with no database. That is a development convenience and a design
statement, not a supported deployment mode; the moment you configure `ConnectionStrings:Sample`,
the same code paths run against PostgreSQL through JauntyQ instead. `apps/sample/Program.cs`
carries the comment explaining why the choice is made in the application rather than in a library.

And `apps/web`:

```
dotnet run --project apps/web
```

It comes up on `https://localhost:5444`. **It must be HTTPS.** The session and antiforgery cookies
are `Secure`-always, so over plain HTTP every page carrying a form returns 500 while `/healthz`
still returns 200 — which reads like one broken page rather than a wrong scheme. That is why the
single profile in `apps/web/Properties/launchSettings.json` is HTTPS, and why there is no HTTP one
beside it to pick by mistake. Run `dotnet dev-certs https --trust` once if the browser objects to
the certificate.

## 4. Run the tests

Three tiers per app, and the project requires all three for every feature — see
[`AGENTS.md`](../../AGENTS.md#testing-required-before-merge). What each is for:

| Tier | What it proves | Needs |
|---|---|---|
| **Unit** (xUnit) | one unit against its own inputs, failure paths included | nothing |
| **Integration** (`WebApplicationFactory`) | the real pipeline, real routing, real middleware | PostgreSQL for the tiers that use it |
| **End-to-end** (Playwright) | a real browser against a real server, including the no-JavaScript path | Chromium, installed once |

Install the browser once:

```
pwsh tests/nativeweb.web.tests.e2e/bin/Debug/net10.0/playwright.ps1 install chromium
```

Then run a tier at a time. Naming a tier rather than globbing is deliberate: a suite that stops
being discovered fails by name instead of vanishing from the run.

```
dotnet test tests/nativeweb.web.tests.unit
dotnet test tests/nativeweb.web.tests.integration
dotnet test tests/nativeweb.web.tests.e2e
```

The full set, and what each returned on the verification run:

| Project | Facts |
|---|---|
| `tests/nativeweb.web.tests.unit` | 509 |
| `tests/nativeweb.web.tests.integration` | 298 |
| `tests/nativeweb.web.tests.e2e` | 64 |
| `tests/nativeweb.render.tests.unit` | 157 |
| `tests/nativeweb.scaffold.tests.unit` | 64 |
| `tests/nativeweb.scaffold.tests.integration` | 31 |
| `tests/nativeweb.scaffold.tests.e2e` | 13 |
| `tests/nativeweb.sample.tests.unit` | 16 |
| `tests/nativeweb.signin.epass.tests.unit` | 184 |
| `tests/nativeweb.signin.epass.tests.integration` | 34 |
| `tests/nativeweb.signin.epass.tests.e2e` | 3 |
| **Total** | **1,360 — 0 failed, 0 skipped** |

Re-measured 2026-08-27, every project run from a clean tree; the `.unit` row had drifted from 496.

A count that has drifted from this table is not automatically a problem; a *failure* is.

**Run one tier at a time, as written above.** Running several e2e projects back-to-back in a
single shell makes them contend, and the whole run fails: measured 2026-08-27, `web.tests.e2e`
and `scaffold.tests.e2e` reported 64 and 13 failures batched, then 64 and 13 *passes* run
individually a few minutes later, on the same commit. A wholly-failing e2e tier is much more
likely to be that than a real break — check it alone before believing it.

---

# The fork

Everything above was shared. From here the two readers diverge, and the divergence is real rather
than cosmetic: **Path A never edits this repository again**, while Path B never runs
`new-product.ps1`. Pick one.

## Path A — build your own product

`skeleton/` is a tokenized product shell: app pipeline, templates, static pages, migration runner,
three test tiers, CI, deploy units and the repo operating files, with `__PRODUCT__`/`__product__`
where your name goes. `tools/new-product.ps1` stamps a copy.

**1. Stamp it.** Bare invocation is the dry run; `-Execute` acts.

```
pwsh tools/new-product.ps1 -Name Probe -Slug probe -Destination ../probe
pwsh tools/new-product.ps1 -Name Probe -Slug probe -Destination ../probe -Execute
```

The dry run lists every file it would copy, stamp and rename — 168 files, from what git tracks or
does not ignore. It also does `git init`, an initial commit, and a `dev` branch, so what you get is
a repository rather than a directory.

**Put it beside your NativeWeb checkout**, as `../probe` above does. The stamped product resolves
the framework libraries and JauntyQ from siblings by default, and step 4 below is much harder if
you skip this.

**2. Create the role and database.** Substitute your slug for `probe` throughout.

```sql
CREATE ROLE probe_app LOGIN PASSWORD '...';
CREATE DATABASE probe_dev OWNER probe_app;
```

**3. Apply the schema.** `migrate.sh` takes its settings from the environment and defaults to the
local dev names, so this usually needs nothing but a password:

```
cd ../probe
PGPASSWORD=... bash db/migrate.sh
```

The first migration runs as `postgres` because it creates the `citext` extension, which the app
role may not; every later one runs as the app role. Re-runs skip what is already applied.

**4. Build and test.** Two things here are not obvious, and both were found by running this rather
than by reading it:

```
export Probe__Db__ConnectionString="Host=127.0.0.1;Database=probe_dev;Username=probe_app;Password=..."
dotnet build
dotnet test tests/probe.tests.unit
dotnet test tests/probe.tests.integration
dotnet test tests/probe.tests.e2e
```

- **The integration tier needs `<Product>__Db__ConnectionString` in the environment.** Without it
  the tier still runs, but `/readyz` reports `ServiceUnavailable` and one fact fails. The
  double-underscore spelling is the environment form of `Probe:Db:ConnectionString`.
- **If your product is *not* beside NativeWeb, set the roots as environment variables, not
  `-p:` flags.** `-p:NativeWebRoot=... -p:JauntyQRoot=...` works for `dotnet build` and then fails
  the e2e tier, because that tier's fixture shells out to its own `dotnet publish` and command-line
  properties do not cross into a child process. Environment variables do:

  ```
  export NativeWebRoot=/path/to/NativeWeb JauntyQRoot=/path/to/jauntyq
  ```

  The symptom if you get this wrong is a publish failure inside the e2e fixture reading
  *"Skipping project … because it was not found"*, which names NativeWeb's libraries and looks
  nothing like a path-resolution problem.

A freshly stamped product on the verification run: **47 unit, 7 integration, 2 e2e — 56 facts,
0 failed.**

**5. Now work in your product, not here.** Its own `README.md` and `AGENTS.md` are yours to fill
in — the one-sentence placeholder at the top of its README is addressed to you. Come back to this
repository only to pick up framework changes.

## Path B — work on `apps/web`

`apps/web` is the epass.io identity provider. Unlike the sample it is a real application with a
real schema, so the database is no longer optional once you go past `/healthz`.

**1. Create the role and database.** Details, including the migration runner's advisory-lock
behaviour, are in [`db/README.md`](../../db/README.md).

```sql
CREATE ROLE nativeweb_app LOGIN PASSWORD '...';
CREATE DATABASE nativeweb_dev OWNER nativeweb_app;
```

**2. Apply the schema.**

```
bash db/migrate.sh
```

**3. Configure the connection string.** It belongs in user-secrets, never in `appsettings.json`:

```
dotnet user-secrets --project apps/web set "ConnectionStrings:NativeWeb" "Host=127.0.0.1;Database=nativeweb_dev;Username=nativeweb_app;Password=..."
```

Every other setting, what it defaults to, and which ones fail loudly versus silently, is in
[`docs/02-reference/configuration.md`](../02-reference/configuration.md). Note the environment-variable spelling there:
`ConnectionStrings__NativeWeb`, double underscores, which is what the integration tier reads in CI.

**4. Run and test** as in steps 3 and 4 above, remembering the HTTPS flag.

**Then read these, in this order**, rather than discovering them by directory listing:
[the architecture](../01-architecture/native-web-architecture.md) for why any of this is shaped as it is,
[the security baseline](../02-reference/security-baseline.md) before touching auth or headers, and
[progressive enhancement](../01-architecture/progressive-enhancement.md) before touching a template. The full index is
[`docs/README.md`](../README.md).

## Next

[`docs/00-quick-start/tutorial-first-feature.md`](tutorial-first-feature.md) walks one feature through every layer
— endpoint, template, query, and a test at each of the three tiers.

---

**Verified.** Every command on this page was executed in the form written, from a clean clone, on
**2026-08-25** — Windows 11 (10.0.26200.8457), .NET SDK 10.0.203, PostgreSQL 18.2, PowerShell
7.6.4, Playwright's bundled Chromium.

Both paths were walked end to end, not read: Path A by stamping a product into `../probe` with its
own role and database and running its three tiers (47 unit, 7 integration, 2 e2e — 56 facts, 0
failed), Path B against `nativeweb_dev`. The two Path A footnotes — the
`<Product>__Db__ConnectionString` requirement and environment variables rather than `-p:` flags —
are both findings from that run rather than inferences from the build files.

The counts table was measured across all eleven projects on that date. The three tiers the
tutorial's own feature touches (`nativeweb.sample.tests.unit`,
`nativeweb.scaffold.tests.integration`, `nativeweb.scaffold.tests.e2e`) were re-run after it landed
and the table carries the later numbers.
