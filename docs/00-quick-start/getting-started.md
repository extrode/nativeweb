# Getting started

From a clean clone to a running application and a green suite. Every command below was executed in
the form written before it was written down; the verification block at the end says on what.

**There is one path here now.** Until 2026-08-31 this guide forked between stamping your own
product and maintaining `apps/web`, the epass.io identity provider that lived in this repository.
It left, permanently, and was not ported anywhere in this tree — see
[`docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`](../decisions/2026-08-31-007-idp-leaves-nativeweb.md).
NativeWeb is a template; you stamp a new product out of `skeleton/` and work in that, not here.

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
(`dotnet build apps/sample`) while you sort the layout out.

## 3. Run it, with no database at all

This is the checkpoint worth reaching first, because it removes PostgreSQL from the list of things
that can be wrong.

```
dotnet run --project apps/sample
```

Then open `http://localhost:5399/` — the home page, the newest published articles rendered through
the framework's own compiled templates. Each title links to `/articles/{slug}`, each tag to
`/tags/{slug}`, and `/tags` lists every tag with its count. `/articles` is the same listing with the
filter posting over htmx and swapping only the list, and it also answers as JSON and CSV.
`http://localhost:5399/admin/articles` is the scaffolded CRUD behind the demo sign-in, where an
article's slug is derived from its title when left blank.

**To edit and see the change without restarting**, one command:

```
dotnet watch run --project apps/sample --no-hot-reload
```

Both halves of that line were measured on 2026-08-31 and neither is decoration:

| | Without it |
|---|---|
| `--no-hot-reload` | The watcher logs nothing on any change, template or `.cs`, and the app keeps serving the previous markup. A scratch console app in the same tree rebuilt on both, so the cause is this project rather than the machine. |
| `<Watch Include="templates\**\*.html" />` in `nativeweb.sample.csproj` | Templates are `AdditionalFiles`, which `dotnet watch`'s default globs do not cover. Editing one produced no file-change line at all. |

A template edit is a source-generator re-run, so the loop is a rebuild and a restart — a few
seconds, not the sub-second swap hot reload advertises. The alternative is not a faster loop; it
is a loop that silently does nothing.

**A missing connection string is not an error
here** — it selects an in-memory store, deliberately, so that the demo, the integration tier and
the browser tier all work with no database. That is a development convenience and a design
statement, not a supported deployment mode; the moment you configure `ConnectionStrings:Sample`,
the same code paths run against PostgreSQL through JauntyQ instead. `apps/sample/Program.cs`
carries the comment explaining why the choice is made in the application rather than in a library.

`apps/sample` is the only deployable project in the tree, runs over plain HTTP, and has no
`Secure`-always cookie policy, so there is no HTTPS step here.

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
pwsh tests/nativeweb.scaffold.tests.e2e/bin/Debug/net10.0/playwright.ps1 install chromium
```

Then run a tier at a time. Naming a tier rather than globbing is deliberate: a suite that stops
being discovered fails by name instead of vanishing from the run.

```
dotnet test --project tests/nativeweb.repo.tests.unit
dotnet test --project tests/nativeweb.render.tests.unit
dotnet test --project tests/nativeweb.web.tests.unit
dotnet test --project tests/nativeweb.forms.tests.unit
dotnet test --project tests/nativeweb.security.tests.unit
dotnet test --project tests/nativeweb.mail.tests.unit
dotnet test --project tests/nativeweb.scaffold.tests.unit
dotnet test --project tests/nativeweb.scaffold.tests.integration
dotnet test --project tests/nativeweb.scaffold.tests.e2e
dotnet test --project tests/nativeweb.sample.tests.unit
```

The full set, and what each returned on the verification run:

| Project | Facts | Covers |
|---|---|---|
| `tests/nativeweb.repo.tests.unit` | 87 | the repository's own build, CI and deploy files |
| `tests/nativeweb.render.tests.unit` | 161 | the template generator and its runtime |
| `tests/nativeweb.web.tests.unit` | 94 | the pipeline, middleware and startup guards |
| `tests/nativeweb.forms.tests.unit` | 85 | form binding and the antiforgery analyzer |
| `tests/nativeweb.security.tests.unit` | 175 | Argon2id, AES-GCM, opaque tokens, blind indexes, bot traps |
| `tests/nativeweb.mail.tests.unit` | 53 | address parsing, header-injection defence, SMTP options |
| `tests/nativeweb.scaffold.tests.unit` | 109 | the CRUD generator |
| `tests/nativeweb.scaffold.tests.integration` | 60 | the generated admin through a real pipeline |
| `tests/nativeweb.scaffold.tests.e2e` | 14 | a real browser, including the no-JavaScript path |
| `tests/nativeweb.sample.tests.unit` | 47 | the reference application |
| **Total** | **885 — 0 failed, 0 skipped** | |

Measured 2026-08-31 from a clean tree, one tier at a time. The ten projects above are every test
project in `NativeWeb.slnx`; nothing else under `tests/` is real.

The security and mail tiers were written on 2026-08-31 and are new: those two libraries had no
coverage at all between the identity provider taking their old suites with it and that date. Three
defects turned up in the writing and are fixed in the same branch — `Passphrases.Verify` throwing
instead of returning false for a stored hash under 45 characters, `Masking.IpAddress` returning
an IPv4 address unmasked when it carried a port, and `EmailTemplates.ParseRecipient` accepting a
line break before the `@` (which MimeKit folds away, so the parsed address is still clean — the
test asserts that rather than a rejection that does not happen).

A count that has drifted from this table is not automatically a problem; a *failure* is.

**Run one tier at a time, as written above.** Running several e2e projects back-to-back in a
single shell can make them contend. Check a wholly-failing e2e tier alone before believing it.

---

# Build your own product

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

**Two things the stamp needs that nothing else tells you.** A global protected-branch git hook
refuses the stamped product's first commit on its own `main`, which fails the stamp with every file
already written; prefix the command with `GIT_ALLOW_PROTECTED=1` if you have one. And a runner with
no git identity fails the same step, which is why CI sets one.

**Or let the check do all of it.** `tools/stamp-check.ts` stamps into `tmp/`, builds and runs the
stamped unit tier, and adds the integration tier with `--with-database` once its role and database
exist. It is how you tell whether `skeleton/` still builds before you stamp the product you mean to
keep -- measured 2026-09-05 at 2.6 s to stamp, 4 to 10 s to build depending on how warm the
framework's own output is, and under 2 s per tier. It prints the test count beside each tier:
1 unit and 6 of the 7 integration facts, the seventh being the one that opens a database.

```
deno run -A tools/stamp-check.ts
```

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
dotnet test --project tests/probe.tests.unit
dotnet test --project tests/probe.tests.integration
dotnet test --project tests/probe.tests.e2e
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

**Then read these, in this order**, rather than discovering them by directory listing:
[the architecture](../01-architecture/native-web-architecture.md) for why any of this is shaped as it is,
[the security baseline](../02-reference/security-baseline.md) for the posture your product inherits, and
[progressive enhancement](../01-architecture/progressive-enhancement.md) before touching a template. The full index is
[`docs/README.md`](../README.md).

## Next

[`docs/00-quick-start/tutorial-first-feature.md`](tutorial-first-feature.md) walks one feature through every layer
— endpoint, template, query, and a test at each of the three tiers.

---

**Verified.** Every command on this page was executed in the form written, from a clean clone, on
**2026-08-25** — Windows 11 (10.0.26200.8457), .NET SDK 10.0.203, PostgreSQL 18.2, PowerShell
7.6.4, Playwright's bundled Chromium.

The product-stamping path was walked end to end, not read: stamping a product into `../probe` with
its own role and database and running its three tiers (47 unit, 7 integration, 2 e2e — 56 facts, 0
failed). The two footnotes — the `<Product>__Db__ConnectionString` requirement and environment
variables rather than `-p:` flags — are both findings from that run rather than inferences from the
build files.

The counts table for this repository's own six suites was re-measured on **2026-08-31**, after
`apps/web` and the seven test projects that covered it left — see
[`docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`](../decisions/2026-08-31-007-idp-leaves-nativeweb.md).
