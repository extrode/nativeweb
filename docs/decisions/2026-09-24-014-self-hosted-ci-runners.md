# 014 — CI runs on self-hosted runners on the dev machine, chosen by repository variable

Date: 2026-09-24
Status: accepted
Supersedes: nothing. It is a switch, not a replacement: unset the variables and every job is back
on GitHub's hosted runners with no commit.

## Decision

**Every job in `.github/workflows/ci.yml` and `publish.yml` reads its runner from a repository
variable, falling back to the hosted runner it used before:**

```yaml
runs-on: ${{ vars.CI_LINUX_RUNNER || 'ubuntu-latest' }}      # test, skeleton-stamp, publish
runs-on: ${{ vars.CI_WINDOWS_RUNNER || 'windows-latest' }}   # aot-publish
```

With the variables set to `nativeweb-linux` and `nativeweb-windows`, the jobs run on two runners
on the dev machine, registered to `extrode/nativeweb` only:

- **Linux:** a Docker container built from `scripts/ci-runner/Dockerfile`, beside a private
  Docker-in-Docker daemon (`scripts/ci-runner/compose.yaml`).
- **Windows:** a native runner installed as a service by
  `scripts/ci-runner/install-windows-runner.ps1`. It uses the machine's Visual Studio 2026 MSVC
  for the NativeAOT link.

## Why

The extrode account's hosted-runner minutes ran out on 2026-09-17, and they reset next month.
Since then every run has died in about five seconds with no step run. The annotation mentions
payments, which is misleading: the cause is the used-up minutes. The alternative was making the
repository public, since public repositories get free minutes. That is a bigger decision, with its
own history-scrub prerequisite (decision 013), so it is not something to take for CI's sake.

**Why a variable rather than a hard-coded label** (unlike another project's runner, which hard-codes
it): the self-hosted runners are a stopgap. When the minutes reset, deleting two variables puts
CI back where it was without a commit, and leaving them set costs nothing.

**Why Docker-in-Docker rather than mounting the host's Docker socket.** The `test` and
`skeleton-stamp` jobs start Postgres as a `services:` container and reach it at `localhost:5432`.
The host's Docker Desktop fails that in two ways:

1. Windows already runs a native PostgreSQL on 5432, so publishing that port fails.
2. Even if it bound, `localhost` inside the runner container is not where Docker Desktop publishes
   ports.

The runner therefore shares the network namespace of its own `docker:dind` daemon, and a
service's published port lands on the runner's `localhost`. Measured before anything was
registered: Postgres 18.6 answered `psql -h localhost` from inside the runner container. As a
side effect, a job cannot see or touch the host's own containers. The daemon listens on a Unix
socket on a shared volume, not on TCP, because dockerd now warns that an unauthenticated TCP
listener will become a startup failure.

**Why a native Windows runner rather than a Windows container.** Docker Desktop runs Linux or
Windows containers, not both. Switching modes would stop every Linux container on the machine,
including the Linux runner itself.

**Three things a persistent runner changes, all handled in the workflows:**

- **The NuGet cache survives between jobs.** A warm `~/.nuget/packages` is what hid the deleted
  `JauntyQ.*` ids from 2026-09-17 to 2026-09-24. Each job's first step points `NUGET_PACKAGES` and
  `NUGET_HTTP_CACHE_PATH` at `$RUNNER_TEMP`, which the runner empties for every job, so every
  restore is cold, as on a hosted runner. The HTTP cache matters too: NuGet trusts it for 30
  minutes, so two runs inside that window could otherwise resolve an id the feed no longer serves.
- **Files outside a checkout survive too.** `skeleton-stamp` writes `ciprobe/` beside the
  `NativeWeb/` checkout, where `actions/checkout` never cleans. The job clears it first, because
  `new-product.ps1` refuses a destination that is not empty.
- **The user-level NuGet config survives too,** and `dotnet nuget add source` exits 1 on a name it
  already holds. So every credential step removes the source first.

`RunnerSelectionTests` pins all of this: the variable-plus-fallback shape of every `runs-on`, the
cold cache before the first SDK command, remove-before-add, the registration URL and labels, and
the compose project name. The name is pinned because another project's runner also lives in a directory
called `ci-runner`, and two compose projects with one name share containers and volumes.

## What this costs

- **Anyone with write access to this private repository can run code on the dev machine.** The
  Linux side runs in a container, but the dind daemon is `privileged`. The Windows runner is a
  native service under NETWORK SERVICE. Acceptable for a private repository with no outside
  contributors. **It is not acceptable once the repository is public:** a fork's pull request
  could then queue a job here. Unset the variables and remove both runners before this repository
  goes public.
- CI now depends on the dev machine being up. A job queued while it is off waits for the runner,
  and GitHub cancels it after 24 hours.
- Cold restores add about a minute per job, the same as on a hosted runner.
