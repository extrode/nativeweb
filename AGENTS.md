# NativeWeb

## Project
NativeWeb is the reference implementation of the "Lean Native Web" architecture: ASP.NET Core
Minimal APIs serving server-rendered HTML enhanced with HTMX and Alpine.js, compiled to a single
self-contained NativeAOT binary, with all data access flowing through the JauntyQ source-generated
data layer over PostgreSQL. Full architecture rationale and the approved execution plan live under
`docs/` (see the architecture and plan docs there).

## Scope: a framework, not a product
**NativeWeb is a framework and holds no product.** This section used to say it was also "the full
ground-up redesign of epass.io", and that dual charter is how a whole identity provider came to
live here under framework names. It left on 2026-08-31; **epass.io owns epass**. What went and why
none of it was ported: `docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`.

The line to hold: Django ships `contrib.auth` and leaves the OAuth2/OIDC server to a third-party
package. Nothing past that line belongs here — no token issuer, consent screen, realms, SSO modes,
client registry or identity schema. Generic primitives (hashing, opaque tokens, bot traps, a data
protector) are framework and welcome; the flow that composes them into a sign-in product is not.

`apps/sample` is the reference application and the only deployable thing in the tree; `skeleton/`
is what `tools/new-product.ps1` stamps. Both exercise the framework. Neither is a product.

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
- JauntyQ resolves from the pinned `JauntyQ.*` packages **by default**; a local checkout is the
  opt-in, via `-p:JauntyQUsePackages=false`. The default flipped on 2026-09-05 when JauntyQ split
  its generator and runtime out to a public core repository
  (`docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md`); checkout mode does not resolve
  against today's sibling `jauntyq/` and will not until that core is cloned and the sentinel in
  `Directory.Build.props` is repointed.

## Tooling runtime: Deno, never Node
Deno is a first-class citizen here and Node is not. Any new script that would have been
JavaScript or TypeScript is a Deno script, run with `deno run -A <script.ts>`, dependencies
imported by URL or from JSR. **Do not introduce `package.json`, a lockfile, `node_modules`, or an
`npm`/`npx` invocation** — the repo has none today (one browser-side `libs/nativeweb.assets/boot.js`
is the only tracked JS, and it is served, not run) and that is worth keeping. Existing tooling in
`tools/` and `scripts/` is Python and PowerShell; leave it, and note that global `AGENTS.md` fixes
cleanup scripts as `.ps1`/`.sh` regardless.

Two places Node appears anyway, neither ours to choose, and both cut to the minimum on 2026-08-20:
`actions/checkout@v7` is the only `uses:` step in either workflow (`runs.using` has no Deno
option), and `Microsoft.Playwright` bundles its own node for the browser driver. **Nothing runs
node20; no step runs Node that does not have to.** Why each survived, and why `setup-dotnet` and
`ilammy/msvc-dev-cmd` did not, is in `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.

## Testing (required before merge)
Every feature ships with all three layers of coverage:
- Unit tests (xUnit).
- Integration tests (`WebApplicationFactory` against a real PostgreSQL instance).
- End-to-end tests (Playwright).

The suites are `render`, `forms`, `scaffold` (unit / integration / e2e) and `sample`. A framework
library's own tier is where its facts belong; `apps/sample` is where they are proved through a
real entry point.

## JauntyQ license constraint
JauntyQ is used from a local checkout only. Permitted use is self-hosted-SaaS use of the
generated output. Never redistribute JauntyQ source or binaries, and never ship this product's
binary to third parties.

## Layout
Flat convention: `apps/` (deployable services), `libs/` (shared libraries), `tests/`, `tools/`.
`db/` holds `sample/` only — the demo schema `apps/sample` binds against. A schema of users,
realms or tokens arriving here is the scope boundary above being crossed.
