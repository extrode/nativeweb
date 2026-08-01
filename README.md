# NativeWeb

<One sentence: what this is and who it is for. If you cannot write it in one sentence, the
project does not know what it is yet.>

## Run it

```
<the single command that gets it running from a clean clone>
```

## Build and test

```
<build command>
<test command>
```

## Layout

| Path | What |
|---|---|
| `src/` | <the code> |
| `tests/` | <the tests> |
| `work/` | Current work: tasklist, status window, backlog, milestones |
| `docs/` | Specs, plans, architecture, decisions, conventions, lessons |
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
