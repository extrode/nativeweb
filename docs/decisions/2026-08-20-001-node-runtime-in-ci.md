# 001 — How much Node runs in CI, and why what's left is left

Date: 2026-08-20
Status: accepted

## Context

`AGENTS.md` binds this repo to Deno and forbids `package.json`, a lockfile, `node_modules` and
any `npm`/`npx` invocation. Node still appeared in two places nobody here chose: GitHub Actions
executes a `uses:` step as a bundled `dist/index.js` on its own Node binary, and
`Microsoft.Playwright` drives the browser through a Node process it ships itself.

The trigger was a deprecation annotation on every CI run: the runner is withdrawing the node20
runtime. The question that followed was the useful one — not "how do we silence this", but "what
is actually running Node, and which of it has to".

## Decision

Three steps went; one stayed. Node20 is gone entirely.

| Step | Outcome | Why |
|---|---|---|
| `actions/checkout@v4` | **bumped to `@v7`** (node24) | Kept — see below |
| `actions/setup-dotnet@v4` | **deleted** | Installed an SDK the image already has |
| `ilammy/msvc-dev-cmd@0b201ec` | **deleted** | Set up a toolchain the ILCompiler finds itself |
| Playwright's bundled node | **kept** | Test-time only, and the alternative costs more |

### `setup-dotnet` — deleted, requirement moved to `global.json`

`actions/runner-images` lists .NET SDK 10.0.110, 10.0.204 and 10.0.302 preinstalled on both
Ubuntu 24.04 (`ubuntu-latest`) and Windows Server 2025 (`windows-latest`). Asking
`dotnet-version: "10.0.x"` installed nothing.

Deleting it would have removed the only written statement of which SDK CI expects, so the
requirement moved to `global.json` — `10.0.100` with `rollForward: latestFeature`, meaning any
installed `10.0.x`. That is strictly more coverage than the workflow had: it governs local builds
too, which `setup-dotnet` never did.

The pin is load-bearing, and demonstrably so. Perturbing it to `rollForward: patch` makes the SDK
resolver itself refuse before any test runs — `10.0.100` exactly is not installed — with
`aka.ms/dotnet/sdk-not-found` and the list of what is. A machine carrying only 9.x fails by name
rather than silently building against the wrong major.

### `msvc-dev-cmd` — deleted, the toolchain finds itself

`microsoft.dotnet.ilcompiler`'s `Microsoft.NETCore.Native.Windows.targets` shells its own bundled
`findvcvarsall.bat`, which asks `vswhere` for the `VC.Tools` component and sets `CppLinker` to an
absolute `link.exe`. It is unconditional unless `IlcUseEnvironmentalTools=true`, which we never
set.

Measured: a full NativeAOT publish of `apps/web` from a shell with `VCINSTALLDIR` empty and no
`cl.exe` on `PATH` links and exits 0 in 56s. If the tools are genuinely absent, the publish stops
with "Platform linker not found" — a named error, never a silent fallback.

It was also actively harmful. The action exported `Platform=x64`; MSBuild reads a bare environment
variable as a property, so an extra `x64` segment appeared in the publish path and the budget step
looked in the wrong place. That failure read as "publish produced nothing" when the publish had
succeeded.

### `actions/checkout` — kept, deliberately

The shell replacement is `git clone` with `$GITHUB_TOKEN`, and it is a worse trade. The action
checks out the merge ref on `pull_request` events, and the skeleton depends on
`persist-credentials: false` when fetching the framework with a PAT. Reimplementing that by hand
is how a token ends up in `.git/config`. One node24 process is cheaper than hand-rolled credential
handling.

There is no way around it in any case: `runs.using` accepts `node20`, `node24`, `docker` and
`composite`, and no Deno action runtime exists. Deno runs happily inside a `run:` step; it cannot
replace the thing that executes the step.

### Playwright — kept

`Microsoft.Playwright` 1.49.0 ships `node` per platform inside the NuGet package —
`.playwright/node/win32_x64/node.exe --version` reports **v22.11.0**, so not node20 either. The
.NET client spawns it and speaks to it over stdio. It runs during `dotnet test` and never reaches
the shipped NativeAOT binary, which makes it build-time tooling in the same sense as `psql` and
the .NET SDK.

The Deno-native alternative is real — `jsr:@astral/astral` speaks CDP with no Node at all — but
our E2E tier is C# xUnit reusing the integration tier's own fixtures in-process: `SignInHost`
(Kestrel, stub provider, self-minted cert), `TestDatabase` over Npgsql, cookie assertions on
`__Host-` attributes. Moving to Deno means rewriting 66 tests in TypeScript, making the host an
external process, and re-proving that `IgnoreHTTPSErrors` and cookie-attribute inspection are
reachable — to remove a binary that never ships. Reconsider only if Playwright itself becomes a
problem.

## Consequences

- Both workflows are node24 throughout, with `actions/checkout@v7` the only `uses:` step, so no
  code from any account but GitHub's runs with the workflow token.
- `ActionPinTests.NoThirdPartyActionsAreUsed` replaced `AtLeastOneThirdPartyActionIsPinned`. With
  the count at zero the old fact could only be kept by weakening it; the SHA theory beside it stays
  as a latent guard for whatever gets added next.
- `SdkPinTests` holds the pin: `global.json` in both this repo and the skeleton must name a
  `10.0.x` version with `rollForward: latestFeature`, and no workflow may reintroduce a
  `setup-dotnet` step — two places naming an SDK is a disagreement that only ever shows up as "it
  builds on my machine".
- `aot-publish` now depends on `vswhere` finding the C++ tools on the hosted image. That is not
  locally verifiable; it fails loudly on the first run if wrong, and reverting is one commit.
