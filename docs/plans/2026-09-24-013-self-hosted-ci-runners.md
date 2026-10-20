# Run NativeWeb CI on self-hosted runners on this machine

## Context

The extrode account's GitHub Actions minutes are used up until next month, so every CI run dies
in ~5s before any step runs. The user chose to run CI on their own machine rather than make
nativeweb public. Decisions already made (AskUserQuestion, 2026-09-24):

- **Linux jobs** (`test`, `skeleton-stamp`, `publish.yml`'s job): a Docker runner container.
- **Windows job** (`aot-publish`): a native Windows runner on this machine, using its VS 2026 MSVC.
  Not a Windows container: Docker Desktop's Windows-containers mode would stop every Linux
  container here (another project's runner, `other-*`).
- **Switching:** a repository variable picks the runner, falling back to GitHub's hosted runners.
  Going back next month is a Settings change with no commit.
- **Scope:** runners registered to `extrode/nativeweb` only.

Precedent: `C:\home\code\other-project\scripts\ci-runner\compose.yaml`, added today for
the same reason. It is a bare `actions-runner` container, and NativeWeb's CI needs more than that.

## What makes NativeWeb's CI different (found while exploring)

1. **Postgres is a `services:` container reached at `localhost:5432`**, in `test` and
   `skeleton-stamp`. The runner therefore needs a Docker daemon. Plain socket-mounting the host's
   Docker won't work: **Windows already has a native Postgres listening on 5432** (PID 14844), so
   Docker Desktop cannot publish `5432:5432`. And even if it could, `localhost` inside the runner
   container is not the VM's localhost.
   → **Docker-in-Docker sidecar.** Runner and a `docker:dind` container share one network
   namespace (`network_mode: service:dind`), with `DOCKER_HOST` pointing at dind. Service ports
   bind inside that shared namespace, so `localhost:5432` works unchanged and nothing binds on
   Windows. Jobs also can't see or touch the host's own containers. Cost: dind runs `privileged`.
2. **The workflow assumes ubuntu-latest's preinstalled tools.** It has no setup-dotnet step and
   uses `psql`, `pwsh` (Playwright's `playwright.ps1`), `sudo apt-get` for clang/zlib and
   `--with-deps`, and `curl` for Deno.
   → a small `Dockerfile` on top of `ghcr.io/actions/actions-runner` adding the .NET 10 SDK (via
   dotnet-install, in the global.json band), PowerShell 7, postgresql-client, clang and
   zlib1g-dev. `sudo` already works in the base image, so the apt steps stay as they are.
3. **A persistent runner reintroduces the warm-cache trap.** A warm `~/.nuget/packages` is exactly
   what hid the JauntyQ restore break for a week. → each job's first step points `NUGET_PACKAGES`
   at `$RUNNER_TEMP` (the runner wipes it per job), so every run restores cold, as on a hosted
   runner. Costs about a minute of restore per job.
4. **The feed credential step is not idempotent on a persistent runner.** `dotnet nuget add source
   jauntyq-github` into the user-level config fails with "already added" the second time.
   → remove the entry before adding it (`remove source … || true`). Dropping the step isn't an
   option yet: NuGet's vulnerability audit queries **every** declared source, and without a
   credential NU1900 fails the build as a warning-as-error (observed today when the root config
   was broken on purpose). That fact goes into the feed-retirement todo line too: retiring means
   removing the source, not just the credential.

## Changes

**Workflows**
- `.github/workflows/ci.yml`: `runs-on` becomes `${{ vars.CI_LINUX_RUNNER || 'ubuntu-latest' }}`
  (`test`, `skeleton-stamp`) and `${{ vars.CI_WINDOWS_RUNNER || 'windows-latest' }}`
  (`aot-publish`). Add a first step per job that sets `NUGET_PACKAGES` via `$GITHUB_ENV` (bash /
  pwsh variants). Make both "Authenticate to GitHub Packages" steps remove-then-add.
- `.github/workflows/publish.yml`: the same `runs-on` expression and `NUGET_PACKAGES` step. Check
  whether it has an auth step, and make it idempotent the same way if so.
- `skeleton/.github/workflows/ci.yml`: **untouched**. A stamped product's runner is its own
  business.

**Runner setup**, new `scripts/ci-runner/`, alongside `scripts/ci/`:
- `compose.yaml`: a `dind` service (privileged, own volume) and a `runner` service (image built
  from the Dockerfile, `network_mode: service:dind`, `DOCKER_HOST=tcp://localhost:2375`,
  `runner-home` volume, `TZ=UTC`). It registers once with
  `--url https://github.com/extrode/nativeweb --name nativeweb-docker --labels nativeweb-linux`,
  using another project's `.runner`-guarded command.
- `Dockerfile`: point 2 above.
- `install-windows-runner.ps1`: downloads the pinned actions-runner win-x64 release into
  `C:\actions-runner\nativeweb`, verifies its SHA-256, and runs `config.cmd --unattended --url …
  --labels nativeweb-windows --runasservice`. The token is a parameter and never stored. Dry run
  by default; `-Execute` acts.
- `README.md`: token command, start/stop, re-register, how to flip back to hosted runners, the
  dind trade-off. `.env` is gitignored (check `.gitignore`).

**Docs and tracking**
- `docs/decisions/2026-09-24-014-self-hosted-ci-runners.md`: why self-hosted, why dind rather than
  the socket, why native Windows, and the security trade-off. The runners are registered to a
  private repository, so only people with write access can queue jobs. dind is privileged and the
  Windows runner runs natively on this machine.
- `work/todo.md`: amend the feed-retirement line (NU1900 means remove the source), and add
  "unset `CI_*_RUNNER` when the minutes reset" (the variables stay harmless if left).
- `CHANGELOG.md`: Unreleased → Changed, one entry.
- Memory: update `project-jauntyq-ci-gap.md` with where CI runs now.

**Tests** (`tests/nativeweb.repo.tests.unit`), new `RunnerSelectionTests.cs`:
- Every job in `ci.yml`/`publish.yml` uses `runs-on: ${{ vars.CI_<OS>_RUNNER || '<hosted>' }}`,
  and the fallback matches the job's OS. This matters because the existing Windows detection in
  the credential tests reads `runs-on`, so the expression has to keep containing `windows`.
- Every job that restores sets `NUGET_PACKAGES` from `RUNNER_TEMP` before its first dotnet step.
- Every `add source` is preceded by a `remove source` of the same name, with the same
  `--configfile`.
- `compose.yaml` registers to `extrode/nativeweb` (OrgReferenceTests already bans the old org
  host; this adds the one expected URL).
Each gets verified by breaking it, then `git checkout --` to revert.

## Steps needing your explicit yes at the time (Confirm-first)

- Minting two runner registration tokens: `gh api -X POST
  repos/extrode/nativeweb/actions/runners/registration-token`. Or you run it with `!`.
- Setting the repo variables: `gh variable set CI_LINUX_RUNNER -b nativeweb-linux`, and the same
  for `CI_WINDOWS_RUNNER`.
- Installing the Windows service (it runs elevated).
- Pushing the branch to trigger the first real run. **That's your call, as always.**

## Verification

1. `docker compose build`, then a spike before registering anything: inside the runner container,
   `docker run -d -p 5432:5432 postgres:18` against dind, then `psql -h localhost`. This proves
   point 1.
2. Branch: repo unit tests (204 + new), `dotnet build` 0 warnings, and each new test broken and
   reverted.
3. After your yes: register both runners, set both variables, push. Watch the run with
   `gh run watch`: `test`, `aot-publish` and `skeleton-stamp` all green on `nativeweb-*` runners.
4. Re-run the same workflow to prove point 4 (a second run on the persistent runner still passes
   the auth step) and point 3 (cold restore again, `NUGET_PACKAGES` under `_temp`).
5. structure-audit, then merge to dev `--no-ff`.

Tier: Standard. The branch will be `chore/self-hosted-ci-runners`.
