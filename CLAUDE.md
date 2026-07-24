# NativeWeb

## Project
NativeWeb is the reference implementation of the "Lean Native Web" architecture: ASP.NET Core
Minimal APIs serving server-rendered HTML enhanced with HTMX and Alpine.js, compiled to a single
self-contained NativeAOT binary, with all data access flowing through the JauntyQ source-generated
data layer over PostgreSQL. It is both a reusable reference template and the full ground-up redesign
of epass.io. Full architecture rationale and the approved execution plan live under `docs/` (see the
architecture and plan docs there).

## Git workflow (MANDATORY)
- `main` is release-only. Never touch except release + versioning.
- `dev` is the integration branch. NEVER commit directly to `dev` or `main`.
- Every change starts on a named branch off `dev`: `git switch -c <type>/<name>`.
- Commit often with clear messages.
- Open work as a PR / merge to `dev` with `--no-ff` — never fast-forward, never squash.
- Get a Fable code review before merging.
- Branch prefixes: `feat/`, `fix/`, `chore/`, `refactor/`, `docs/`.
- Release: `dev` -> `main` with `--no-ff` + a semver tag.
- No AI attribution in commit messages.

## Parallel agents (worktrees)
- Use `git worktree` for any parallel / multi-agent work — never share one working tree across agents.
- Location: `<project_root>/.worktrees/<branch-name>`.
- Remove the worktree once its branch merges; don't leave stale worktrees.

## Stack (binding)
- .NET 10 / C# 14, NativeAOT (`PublishAot=true`). No trim/AOT analyzer warnings allowed —
  `TreatWarningsAsErrors` is on repo-wide.
- Minimal APIs only. No MVC, no Controllers, no Blazor, no Razor Pages.
- JauntyQ for ALL data access. No EF Core, no Dapper, no hand-written ADO.NET.
- HTMX + Alpine.js, vendored into the repo. No CDN references.
- PostgreSQL only.
- JauntyQ resolves dual-mode: local checkout (`$(JauntyQRoot)`, probed in `Directory.Build.props`)
  for co-development, or pinned `JauntyQ.*` packages in CI via `-p:JauntyQUsePackages=true`.

## Testing (required before merge)
Every feature ships with all three layers of coverage:
- Unit tests (xUnit).
- Integration tests (`WebApplicationFactory` against a real PostgreSQL instance).
- End-to-end tests (Playwright).

## JauntyQ license constraint
JauntyQ is used from a local checkout only. Permitted use is self-hosted-SaaS use of the
generated output. Never redistribute JauntyQ source or binaries, and never ship this product's
binary to third parties.

## Layout
Flat convention: `apps/` (deployable services), `libs/` (shared libraries), `tests/`, `tools/`.
