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

## Status

See `work/status/current.md` for what is being worked on right now, and `CHANGELOG.md` for
what has shipped.

---

**Local READMEs.** Any directory whose purpose is not obvious from its name gets its own
`README.md`, kept to a few lines: what belongs here, what does not, and the naming rule if
there is one. The test is simple — if someone would have to open three files to work out what
a directory is for, it needs one.
