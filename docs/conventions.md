# Conventions — NativeWeb

Mechanical how-we-work rules. Defaults are inherited from ~/.claude/templates/project-rules.md;
this file records project-specific deltas. Binding principles live in docs/constitution.md;
project memory lives in docs/lessons/.

## File & directory naming
- kebab-case for files and dirs: `mail-pipeline.md`, `data-model.sql`.
- Except where an established convention dictates otherwise: `README.md`, `CHANGELOG.md`,
  `LICENSE.md`, `Directory.Build.props`, or language-mandated casing (C# `PascalCase.cs`).
- Unsure? Match the nearest existing sibling file.

## Git workflow
- `main` is release-only; `dev` is integration. Never commit directly to either.
- All work on named branches off `dev` (`feat/ fix/ chore/ docs/ refactor/ test/`). Commit often.
- Merge completed branches back to `dev` with `--no-ff`. Always merge once a branch is complete —
  never leave finished work unmerged.
- Release: `dev` -> `main` `--no-ff` + semver tag. No AI attribution. Ask before deleting branches.

## Cleanup policy
- NEVER auto-delete files, branches, or artifacts (the guards block it anyway).
- Instead: append every cleanup candidate to a running list, then generate a reviewed PowerShell
  script at `scripts/cleanup/cleanup-<milestone>.ps1` for the user to run manually.
- **Fixed interface, every script, both shells.** Dry run is what a bare invocation does — there
  is no flag to request one. `--execute` / `-e` is the only way to act; not `-Apply`, `-Force`,
  `--yes` or `-WhatIf`. Anything irreversible beyond that (deleting untracked files, `git rm`,
  dropping tables, rewriting history) needs a second flag named for what it does, on top of
  `--execute`. A dry run always exits 0; `--execute` with a failed precondition exits non-zero.
- Script rules: `$ErrorActionPreference='Stop'` (or `set -euo pipefail`); safety gate (refuse if
  not on the expected branch); numbered sections; `git branch -d` (never `-D`); print the
  remaining state at the end; header comment naming the work and date that produced it.

## Docs layout
Specs, plans, designs and architecture notes live under `docs/`, committed, never in a
dot-directory and never untracked — they exist to be reviewed in a diff.

```
docs/
  specs/001-a-new-way-for-the-api/          # one dir per spec
    001-spec.md  001-plan.md  001-tasks.md
  plans/2026-07-28-001-workflow-routing.md  # plan-mode work with no spec
  architecture/001-storage-model.md
```

- Numbered always, zero-padded to three, next number allocated by scanning the directory.
- Dated as well where entries accumulate rather than being revised (`plans/`, `decisions/`);
  not in `specs/`, where a spec is updated in place. Date first, `YYYY-MM-DD`, so it sorts.
- An approved plan-mode plan is promoted out of its generated scratch name into `docs/plans/`
  and committed with the branch's first commit.

## Project visibility

`work/` holds the files that answer "what is going on here" without reading the code.
They are updated as part of the work, in the same branch, not batched to the end of a session.

```
CHANGELOG.md            # what shipped. At the ROOT, and written at release, not per commit.
work/
  tasklist.md           # every task for the body of work underway. Stays put.
  milestones.md         # where this is going. Coarse; changes rarely.
  todo.md               # unscheduled backlog. Out-of-scope notes land here.
  status/
    previous.md         # the task that just finished
    current.md          # the ONE task in flight
    next.md             # the ONE task queued
  archive/
    2026-07-28-storage-move.md   # a completed tasklist, one per shipped body of work
```

`tasklist.md` is the task breakdown for the work currently underway. The `status/` trio is a
three-deep sliding window over it, one task each: `current` -> `previous`, `next` -> `current`,
next unchecked tasklist entry -> `next`. The tasklist does not rotate; it is emptied and refilled
when that body of work ships. Durable-tier work already has one at
`docs/specs/<NNN>-*/<NNN>-tasks.md` — point at it rather than keeping a second copy.

An item's path is `todo.md` -> `tasklist.md` -> `status/next.md` -> `status/current.md` ->
`status/previous.md` -> `CHANGELOG.md`.

When `tasklist.md` is fully ticked: summarise it into `CHANGELOG.md` under `## [Unreleased]`, move
it to `work/archive/YYYY-MM-DD-<slug>.md`, and start a fresh one.

`/status` shows the rotation and flags staleness; `/status roll` advances it. `docs/project/` and
`docs/status/` are the pre-2026-07-28 locations — read them if that is what the repo has, but do
not move them without being asked, because the paths are referenced elsewhere.

**Check the layout after every task, not at the end of the body of work.** A file left in the
wrong place for six tasks is a file nobody will move.

## READMEs

`README.md` at the repo root, always: what this is, how to run it, how to build it, and the
layout table. Someone arriving cold should not have to read code to learn what the project is.

A local `README.md` in any subdirectory whose purpose is not obvious from its name — a few lines
saying what belongs there, what does not, and the naming rule if there is one. The test: if
someone would have to open three files to work out what a directory is for, write one.

Not every directory. `src/` and `tests/` explain themselves; `work/archive/` and
`scripts/cleanup/` do not.

## Instruction file

`AGENTS.md` is the instruction file. `CLAUDE.md` beside it holds the single line `@AGENTS.md`.

Claude Code injects `CLAUDE.md` into context at load and does **not** inject `AGENTS.md` — but it
does honour the `@` import inside `CLAUDE.md`, which pulls the real file in with no tool call.
Verified empirically with a canary in each file, both directions. Every other agent tool reads
`AGENTS.md` directly. So there is one source of truth and a one-line shim nobody edits.

## Specs freshness
- When code diverges from a shipped spec, update `docs/specs/NNN-*/spec.md` (bump its `Status:`
  and add a `last-verified: <date>` line). `/spec-status` flags any spec older than its own
  plan/tasks as ⚠ stale.

## Response compression and BREACH (spec 003 R32)

Both apps compress `text/html` only, Brotli-first, with `EnableForHttps=true`. Compressing
secret-bearing HTML over TLS is the precondition for BREACH-class attacks, so the acceptance is
deliberate and bounded:

- The only per-request secrets in rendered HTML are the antiforgery token and the CSP nonce.
  ASP.NET Core antiforgery request tokens are masked per issue (a fresh random XOR each render),
  so the byte sequence an attacker would need to converge on changes every response — the
  compression-oracle loop BREACH depends on never stabilises. The nonce is single-use by
  definition.
- BREACH additionally requires attacker-chosen text reflected into the same response as the
  secret. The apps render user input HTML-encoded through typed template params, and error pages
  echo nothing request-derived.
- OAuth token/introspection responses are JSON and not on the mime list; they are never
  compressed.

If a future page reflects request input verbatim alongside a stable secret, that page — not the
mime list — is the thing to fix.

## Caching of token-bearing pages (spec 003 R30)

No shared-cache headers and no ETags on session-bound HTML: `SecurityHeadersMiddleware` stamps
`Cache-Control: no-store` (+ `Pragma: no-cache` where RFC 6749 demands) on every sensitive
content type and redirect. Fingerprinted static assets keep their long-lived immutable caching —
the split is by content type, never blanket. Static *pages* (R38) get `no-cache` + ETag
revalidation instead: they carry no tokens, so revalidation is safe and cheap.

## Options validation (spec 003 R34)

The apps deliberately have no `IOptions<T>` classes to `ValidateOnStart`: configuration is read
imperatively in `Program.cs` with fail-fast guards that throw before `Build()` on malformed
values (see `StartupConfigGuardTests`, which boot the real binary against broken config and
assert the refusal). A future options class must either keep that shape or register with
`.ValidateOnStart()` — a config error surfacing as a first-request hang is the AOT lesson this
rule closes.
