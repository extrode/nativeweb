# Getting started

From a clean clone to a running application and a green suite, then to a product of your own.

NativeWeb is a framework. You run the reference app in `apps/sample` to see it work, then stamp a
new product out of `skeleton/` and work in that, not here.

## Before you start

The requirements are in the [README](../../README.md#requirements). PostgreSQL is optional for
everything in steps 1 to 3.

## 1. Clone

```bash
git clone <this repository> NativeWeb
cd NativeWeb
```

Anywhere you like. JauntyQ restores from packages, so nothing here looks for a sibling checkout
unless you build with `-p:JauntyQUsePackages=false`.

The directory can be called `NativeWeb` or `nativeweb`. A stamped product probes for both. It
matters only on a case-sensitive filesystem with a third name: then set `NativeWebRoot` instead of
relying on the probe.

## 2. Build

```bash
dotnet build
```

JauntyQ resolves from the pinned `Extrode.JauntyQ.*` packages on nuget.org. No credential is
needed.

## 3. Run it with no database

```bash
dotnet run --project apps/sample
```

Open `http://localhost:5399/`. The home page shows the newest published articles, rendered through
compiled templates.

- Each title links to `/articles/{slug}` and each tag to `/tags/{slug}`. `/tags` lists every tag
  with its count.
- `/articles` is the same listing with a filter. The filter posts over htmx and swaps only the
  list. The route also answers as JSON and CSV.
- `/admin/articles` is the scaffolded CRUD behind the demo sign-in. A blank slug is derived from
  the title.

To edit and see the change without a manual restart:

```bash
dotnet watch run --project apps/sample --no-hot-reload
```

Both parts matter. Without `--no-hot-reload` the watcher logs nothing on a change and the app keeps
serving the old markup. Templates are `AdditionalFiles`, which `dotnet watch` does not track by
default, so `nativeweb.sample.csproj` adds `<Watch Include="templates\**\*.html" />` (and the same
for `locales\*.json`). A template edit re-runs the source generator, so the loop is a rebuild and a
restart, a few seconds.

**A missing connection string is not an error.** It selects an in-memory store, so the demo and the
test tiers work with no database. It is a development convenience, not a supported deployment mode.
Configure `ConnectionStrings:Sample` and the same code paths run against PostgreSQL through JauntyQ.
`apps/sample/Program.cs` explains why the choice is made in the application and not in a library.

`apps/sample` runs over plain HTTP, so there is no HTTPS step.

## 4. Run the tests

Every feature has up to three tiers, and the project requires all of them. See
[`AGENTS.md`](../../AGENTS.md#testing-required-before-merge).

| Tier | What it proves | Needs |
|---|---|---|
| Unit (xUnit) | one unit against its own inputs, failure paths included | nothing |
| Integration (`WebApplicationFactory`) | the real pipeline, routing and middleware | PostgreSQL, for the projects that use it |
| End-to-end (Playwright) | a real browser against a real server, including the no-JavaScript path | Chromium, installed once |

Install the browser once, after building the e2e project:

```bash
pwsh tests/nativeweb.scaffold.tests.e2e/bin/Debug/net10.0/playwright.ps1 install chromium
```

The integration and e2e projects that touch the sample database need `nativeweb_sample_test`.
[`db/sample/README.md`](../../db/sample/README.md) has the commands that create it.

Run one project at a time. A project that stops being discovered then fails by name instead of
vanishing from a glob. Every project is named `nativeweb.<lib>.tests.<tier>`:

```bash
# unit tier
dotnet test --project tests/nativeweb.repo.tests.unit
dotnet test --project tests/nativeweb.render.tests.unit
dotnet test --project tests/nativeweb.web.tests.unit
dotnet test --project tests/nativeweb.forms.tests.unit
dotnet test --project tests/nativeweb.security.tests.unit
dotnet test --project tests/nativeweb.mail.tests.unit
dotnet test --project tests/nativeweb.scaffold.tests.unit
dotnet test --project tests/nativeweb.permissions.tests.unit
dotnet test --project tests/nativeweb.signin.tests.unit
dotnet test --project tests/nativeweb.sample.tests.unit

# integration tier
dotnet test --project tests/nativeweb.forms.tests.integration
dotnet test --project tests/nativeweb.scaffold.tests.integration
dotnet test --project tests/nativeweb.permissions.tests.integration
dotnet test --project tests/nativeweb.signin.tests.integration
dotnet test --project tests/nativeweb.sample.tests.integration

# end-to-end tier
dotnet test --project tests/nativeweb.forms.tests.e2e
dotnet test --project tests/nativeweb.scaffold.tests.e2e
dotnet test --project tests/nativeweb.permissions.tests.e2e
dotnet test --project tests/nativeweb.signin.tests.e2e

# NativeAOT tier
dotnet test --project tests/nativeweb.signin.tests.aot
```

Do not run several e2e projects back to back in one shell: they can contend. If a whole e2e tier
fails, re-run it alone before believing it.

---

# Build your own product

`skeleton/` is a tokenized product shell: app pipeline, templates, static pages, migration runner,
three test tiers, CI, deploy units and the repo operating files, with `__PRODUCT__` and
`__product__` where your name goes. `tools/new-product.ps1` stamps a copy.

**1. Stamp it.** A bare invocation is a dry run; `-Execute` acts.

```bash
pwsh tools/new-product.ps1 -Name Probe -Slug probe -Destination ../probe
pwsh tools/new-product.ps1 -Name Probe -Slug probe -Destination ../probe -Execute
```

The dry run lists every file it would copy, stamp and rename. The real run also does `git init`, an
initial commit and a `dev` branch, so you get a repository and not just a directory.

Two things the stamp needs that nothing else tells you. A global protected-branch git hook refuses
the first commit on the product's `main`, which fails the stamp with every file already written:
prefix the command with `GIT_ALLOW_PROTECTED=1` if you have such a hook. A runner with no git
identity fails the same step, which is why CI sets one.

To check that `skeleton/` still builds before you stamp the product you mean to keep:

```bash
deno run -A tools/stamp-check.ts
```

It stamps into `tmp/`, builds, and runs the stamped unit tier. Add `--with-database` to run the
integration tier too, once its role and database exist.

Put the product beside your NativeWeb checkout, as `../probe` does above. The stamped product
resolves the framework libraries from that sibling by default.

**2. Create the role and database.** Substitute your slug for `probe` throughout.

```sql
CREATE ROLE probe_app LOGIN PASSWORD '...';
CREATE DATABASE probe_dev OWNER probe_app;
```

**3. Apply the schema.** `migrate.sh` reads its settings from the environment and defaults to the
local dev names, so this usually needs only a password:

```bash
cd ../probe
PGPASSWORD=... bash db/migrate.sh
```

The first migration runs as `postgres` because it creates the `citext` extension, which the app
role may not. Every later one runs as the app role. Re-runs skip what is already applied.

**4. Build and test.**

```bash
export Probe__Db__ConnectionString="Host=127.0.0.1;Database=probe_dev;Username=probe_app;Password=..."
dotnet build
dotnet test --project tests/probe.tests.unit
dotnet test --project tests/probe.tests.integration
dotnet test --project tests/probe.tests.e2e
```

- **The integration tier needs `<Product>__Db__ConnectionString` in the environment.** Without it
  the tier runs, but `/readyz` reports `ServiceUnavailable` and one fact fails. The double
  underscore is the environment form of `Probe:Db:ConnectionString`.
- **If the product is not beside NativeWeb, set the root as an environment variable, not a `-p:`
  flag.** `-p:NativeWebRoot=...` works for `dotnet build` and then fails the e2e tier, because that
  tier's fixture runs its own `dotnet publish` and command-line properties do not cross into a
  child process. Environment variables do:

  ```bash
  export NativeWebRoot=/path/to/NativeWeb
  ```

  The symptom is a publish failure inside the e2e fixture reading *"Skipping project … because it
  was not found"*, which names NativeWeb's libraries and does not look like a path problem.

**5. Work in your product, not here.** Its own `README.md` and `AGENTS.md` are yours to fill in.
Come back to this repository only to pick up framework changes.

Then read, in this order: [the architecture](../01-architecture/native-web-architecture.md) for why
the stack is shaped as it is, [the security baseline](../02-reference/security-baseline.md) for the
posture your product inherits, and [progressive enhancement](../01-architecture/progressive-enhancement.md)
before touching a template. The full index is [`docs/README.md`](../README.md).

## Next

[`tutorial-first-feature.md`](tutorial-first-feature.md) walks one feature through every layer:
endpoint, template, query, and a test at each of the three tiers.

To bring NativeWeb into an app you already have, see
[`adopting-in-an-existing-app.md`](adopting-in-an-existing-app.md).
