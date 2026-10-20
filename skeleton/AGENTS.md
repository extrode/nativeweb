# __PRODUCT__

## Project
__PRODUCT__ is <one paragraph: what the product does, who it is for, and what it replaces or
enables>. It is built on the "Lean Native Web" architecture: ASP.NET Core Minimal APIs serving
server-rendered HTML enhanced with HTMX and Alpine.js, compiled to a single self-contained
NativeAOT binary, with all data access flowing through the JauntyQ source-generated data layer
over PostgreSQL.

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
- The NativeWeb framework libs (security, render + render generator) resolve dual-mode:
  local checkout (`$(NativeWebRoot)`, probed in `Directory.Build.props`) for co-development, or
  pinned `NativeWeb.*` packages via `-p:NativeWebUsePackages=true`.
- JauntyQ is the other way round: the pinned `Extrode.JauntyQ.*` packages on nuget.org are the
  default, and a local checkout (`$(JauntyQRoot)`) is the opt-in via `-p:JauntyQUsePackages=false`.

## Tooling runtime: Deno, never Node
Deno is a first-class citizen here and Node is not. Any new script that would have been
JavaScript or TypeScript is a Deno script, run with `deno run -A <script.ts>`, dependencies
imported by URL or from JSR. **Do not introduce `package.json`, a lockfile, `node_modules`, or an
`npm`/`npx` invocation.** The only tracked JS is what the browser loads, and that is served, not
run. Two places Node appears regardless, neither ours to choose: GitHub Actions executes a
`uses:` action as a bundled `dist/index.js` on its own Node binary (`runs.using` has no Deno
option, so `actions/checkout` cannot be moved off it), and `Microsoft.Playwright` drives a
Node-based browser driver under the .NET API. Keep it to that. `ci.yml` deliberately has no
`setup-dotnet` step — the hosted images already ship the SDK and `global.json` states which band
is required — and no third-party action at all.

## Testing (required before merge)
Every feature ships with all three layers of coverage:
- Unit tests (xUnit).
- Integration tests (`WebApplicationFactory` against a real PostgreSQL instance).
- End-to-end tests (Playwright).

## JauntyQ license constraint
Permitted use of JauntyQ is self-hosted-SaaS use of the generated output. Never redistribute JauntyQ source or binaries, and never ship this product's
binary to third parties.

## Layout
Flat convention: `apps/` (deployable services), `libs/` (shared libraries), `tests/`, `db/`, `deploy/`.
