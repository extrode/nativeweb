# NativeWeb

## Project
NativeWeb is the reference implementation of the "Lean Native Web" architecture: ASP.NET Core
Minimal APIs serving server-rendered HTML enhanced with htmx, compiled to a single self-contained
NativeAOT binary, with all data access going through the JauntyQ source-generated data layer over
PostgreSQL. The architecture and its reasoning are in
`docs/01-architecture/native-web-architecture.md`.

## Scope: a framework, not a product
**NativeWeb is a framework and holds no product.** `apps/sample` is the reference application and
the only deployable thing in the tree; `skeleton/` is what `tools/new-product.ps1` stamps. Both
exercise the framework. Neither is a product.

The line to hold: Django ships `contrib.auth` and leaves the OAuth2/OIDC server to a third-party
package. NativeWeb is the same. `libs/nativeweb.signin` is a relying party (the client), and
nothing past that line belongs here: no token issuer, consent screen, realms, SSO modes, client
registry or identity schema. Generic primitives (hashing, opaque tokens, bot traps, a data
protector) are framework and welcome; the flow that composes them into a sign-in product is not.
An identity provider used to live here and was moved out on 2026-08-31; the reasoning is in
`docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`.

## Git workflow (mandatory)
- `main` is release-only. Never touch it except to release.
- `dev` is the integration branch. Never commit directly to `dev` or `main`.
- Every change starts on a named branch off `dev`: `git switch -c <type>/<name>`.
- Branch prefixes: `feat/`, `fix/`, `chore/`, `refactor/`, `docs/`.
- Commit often with clear, conventional messages. No AI attribution in commit messages.
- Merge to `dev` with `--no-ff`. Never fast-forward, never squash.
- Get a code review before merging.
- Release: merge `dev` into `main` with `--no-ff` and add a semver tag.

## Parallel agents (worktrees)
- Use `git worktree` for any parallel or multi-agent work. Never share one working tree.
- Location: `<project_root>/.worktrees/<branch-name>`.
- Remove the worktree once its branch merges.

## Stack (binding)
- .NET 10 / C# 14, NativeAOT (`PublishAot=true`). No trim or AOT analyzer warnings:
  `TreatWarningsAsErrors` is on repo-wide.
- Minimal APIs only. No MVC, no controllers, no Blazor, no Razor Pages.
- JauntyQ for all data access. No EF Core, no Dapper, no hand-written ADO.NET.
- htmx and Alpine.js, vendored into the repo. No CDN references. Alpine.js is loaded by the sample
  and the skeleton but no page uses it yet.
- PostgreSQL only.
- JauntyQ resolves from the pinned `Extrode.JauntyQ.*` packages on nuget.org by default. A local
  `jauntyq` checkout next to this one is opt-in, via `-p:JauntyQUsePackages=false`
  (`docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md`).

## Tooling runtime: Deno, never Node
Any new script that would have been JavaScript or TypeScript is a Deno script, run with
`deno run -A <script.ts>`, with dependencies imported by URL or from JSR. **Do not introduce
`package.json`, a lockfile, `node_modules`, or an `npm`/`npx` invocation.** The repo has none.
The only first-party JavaScript is browser code that is served as-is:
`libs/nativeweb.web/wwwroot/_nativeweb/boot.js` and the sample's `wwwroot/js/theme.js` and
`wwwroot/js/htmx-antiforgery.js`. Older tooling in `tools/` and `scripts/` is Python and
PowerShell; leave it as it is.

Node appears in two places that are not ours to choose: `actions/checkout` is the only `uses:` step
in the workflows (`runs.using` has no Deno option), and `Microsoft.Playwright` bundles its own Node
for the browser driver. No step runs Node that does not have to
(`docs/decisions/2026-08-20-001-node-runtime-in-ci.md`).

## Testing (required before merge)
Every feature ships with all three tiers:
- Unit tests (xUnit).
- Integration tests (`WebApplicationFactory` against a real PostgreSQL instance).
- End-to-end tests (Playwright, against the published NativeAOT binary).

Test projects live in `tests/` as `nativeweb.<library>.tests.<tier>`. A library's own tests are
where its facts belong; `apps/sample` is where they are proved through a real entry point.

## JauntyQ license constraint
JauntyQ's source and packages are under ISL-R 1.2. Code its generator emits into this repository is
ours under its Output Exception, and `Extrode.JauntyQ.Runtime` may ship inside built output under
its Redistribution Exception. Nothing else of JauntyQ's may be committed, vendored or
redistributed from here, including files from a local checkout.

## Layout
- `apps/`: deployable apps (`apps/sample` only).
- `libs/`: framework libraries and their source generators.
- `tests/`: test projects, per library and tier.
- `skeleton/`: the product shell `tools/new-product.ps1` stamps.
- `db/`: `sample/` only, the demo schema `apps/sample` binds against. A schema of users, realms or
  tokens arriving here is the scope boundary above being crossed.
- `docs/`: guides, reference, architecture, decisions, specs and plans.
- `tools/`, `scripts/`: developer tooling and operational scripts.
- `work/`: task list, status and backlog.
