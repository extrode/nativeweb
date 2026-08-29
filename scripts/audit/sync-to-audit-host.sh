#!/usr/bin/env bash
# Mirror this repository to the audit host.
#
# Run on the workstation. Pushes every ref to audit-host:~/audit/mirror/nativeweb.git, creating the bare
# repository on first use. It never touches `origin`, and there is no flag that makes it.
#
# Why push rather than let the audit host pull from GitHub (R1). Finished work in this repository
# is merged into `dev` and left unpushed by policy, so a host that pulls from `origin` audits the
# state the maintainer finished with days ago. The direction also gives R1 for free: the
# workstation can reach audit-host, audit-host cannot reach the workstation, and audit-host holds no credential that
# reaches `origin`. Nothing the audit does on that host can reach the real repository.
#
# Bare invocation is the dry run. --execute pushes.

set -euo pipefail

HOST="${AUDIT_HOST:-audit-host.local}"
REMOTE_PATH="${AUDIT_MIRROR_PATH:-audit/mirror/nativeweb.git}"
EXECUTE=0

usage() {
  cat <<'EOF'
Usage: scripts/audit/sync-to-audit-host.sh [--execute] [--host <host>]

  (no flags)      dry run: report what would be pushed, change nothing
  --execute, -e   push every ref to the audit host's mirror
  --host <host>   audit host (default audit-host.local, or $AUDIT_HOST)

Never pushes to origin. Pushing to origin is the maintainer's, always.
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --execute|-e) EXECUTE=1 ;;
    --host) HOST="$2"; shift ;;
    --help|-h) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

MIRROR="${HOST}:${REMOTE_PATH}"
BRANCHES="$(git for-each-ref --format='%(refname:short)' refs/heads | tr '\n' ' ')"

echo "repository : $REPO_ROOT"
echo "mirror     : $MIRROR"
echo "branches   : $BRANCHES"

if [ "$EXECUTE" -eq 0 ]; then
  echo
  echo "Dry run. Nothing was pushed. Re-run with --execute to mirror to the audit host."
  exit 0
fi

# Create the bare repository on first use. `git init --bare` is idempotent: on an existing
# repository it reinitialises and leaves every ref alone.
ssh -o BatchMode=yes "$HOST" "mkdir -p '$(dirname "$REMOTE_PATH")' && git init --bare --quiet '$REMOTE_PATH'"

# --mirror, so a branch deleted here disappears there and the audit never reads a dead branch.
# It is also why this must not be pointed at origin: --mirror to a shared remote deletes refs.
git push --mirror "$MIRROR"

echo "mirrored $(echo "$BRANCHES" | wc -w | tr -d ' ') branches to $MIRROR"
