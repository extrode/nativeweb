# Self-hosted CI runners

Two runners on the dev machine, used while the account's GitHub Actions minutes are out. Why they
exist and what they cost: `docs/decisions/2026-09-24-014-self-hosted-ci-runners.md`.

| Runner | Label | Jobs | Setup |
|---|---|---|---|
| Linux, Docker | `nativeweb-linux` | `test`, `skeleton-stamp`, `publish` | `compose.yaml` + `Dockerfile` |
| Windows, native service | `nativeweb-windows` | `aot-publish` | `install-windows-runner.ps1` |

A job uses a runner only while the matching repository variable names its label. Otherwise it
falls back to `ubuntu-latest` / `windows-latest`.

## Registration tokens

Each runner needs one token for its first start. A token is valid for an hour and is used once:

```
gh api -X POST repos/extrode/nativeweb/actions/runners/registration-token --jq .token
```

## Linux runner

```
cd scripts/ci-runner
echo RUNNER_TOKEN=<token> > .env
docker compose up -d --build
```

`.env` is gitignored. After the first start the registration lives in the `runner-home` volume, so
restarts need no token. The compose project is named `nativeweb-ci-runner` on purpose: another project's
runner sits in a directory with the same name, and an unnamed project would take over its
containers.

- Logs: `docker compose logs -f runner`
- Stop: `docker compose down` (keeps the registration)
- Re-register: `docker compose down -v`, remove `nativeweb-docker` under the repository's
  Settings > Actions > Runners, then start again with a fresh token

## Windows runner

From an elevated PowerShell:

```
pwsh scripts/ci-runner/install-windows-runner.ps1 -Token <token>            # dry run
pwsh scripts/ci-runner/install-windows-runner.ps1 -Token <token> -Execute
```

It installs to `C:\actions-runner\nativeweb` and registers a service running as NETWORK SERVICE.
The runner release is pinned and checked against its SHA-256. To remove it:
`C:\actions-runner\nativeweb\config.cmd remove --token <removal-token>`. The removal token comes
from the same API, at `.../runners/remove-token`.

## Switching CI between these runners and GitHub's

```
gh variable set CI_LINUX_RUNNER   -R extrode/nativeweb -b nativeweb-linux
gh variable set CI_WINDOWS_RUNNER -R extrode/nativeweb -b nativeweb-windows

gh variable delete CI_LINUX_RUNNER   -R extrode/nativeweb   # back to ubuntu-latest
gh variable delete CI_WINDOWS_RUNNER -R extrode/nativeweb   # back to windows-latest
```

**Delete both variables and remove both runners before this repository goes public.** On a public
repository, a fork's pull request could run code on this machine.
