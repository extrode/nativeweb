# The Linux runner image for .github/workflows/ci.yml. The workflow assumes what GitHub's
# ubuntu-latest has preinstalled, and it has no setup step for any of it: the .NET 10 SDK, pwsh
# (Playwright's install script is playwright.ps1), psql, and gh (publish.yml's preconditions call
# `gh api`). clang and zlib1g-dev are installed
# by the workflow itself with sudo, but they are baked in too, so that step is a no-op here.
FROM ghcr.io/actions/actions-runner:latest

USER root

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates curl wget apt-transport-https \
      postgresql-client clang zlib1g-dev libicu74 \
 && wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
 && dpkg -i packages-microsoft-prod.deb \
 && rm packages-microsoft-prod.deb \
 && apt-get update \
 && curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
      -o /usr/share/keyrings/githubcli-archive-keyring.gpg \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
      > /etc/apt/sources.list.d/github-cli.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends powershell gh \
 && rm -rf /var/lib/apt/lists/*

# global.json pins 10.0.100 with rollForward latestFeature, so the newest 10.0 SDK satisfies it.
# Installed system-wide, not under /home/runner, which is a volume and would shadow it.
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
 && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet

USER runner
