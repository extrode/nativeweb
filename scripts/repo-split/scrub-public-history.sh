#!/usr/bin/env bash
set -euo pipefail

# Scrubs NativeWeb's git history before the repository goes public, matching Jaunty's and
# JauntyQ's own precedent of doing this before the first public push rather than after (see
# docs/decisions/2026-09-17-013-history-filtered-before-first-public-release.md).
#
# Three transforms, one git-filter-repo pass over the real repo:
#   - path removal:     scrub-paths-idp.txt + scrub-paths-devmarks.txt (--invert-paths)
#   - text replacement: scrub-text-rules.txt    (two real infrastructure IPs)
#   - message rewrite:  scrub-message-rules.txt (the pre-Extrode org identity, "extrode-legacy-org")
#
# Unlike JauntyQ's own scrub (pure text/message replacement, nothing removed), this one removes
# whole directories from history -- the identity provider that left NativeWeb on 2026-08-31 and
# DevMarks, a smaller product spun out to manzil.my. Removed history is not just discarded: this
# script builds a full-commit-history archive of exactly those paths, to a location OUTSIDE this
# repository, before the real repo is touched. The real rewrite refuses to run if that archive is
# missing or fails to verify.
#
# Usage:
#   ./scrub-public-history.sh                              dry run + build/verify the archive
#                                                           (default, no changes to this repo)
#   ./scrub-public-history.sh --archive-dir <path>          write the archive bundles somewhere
#                                                           other than the default sibling dir
#   ./scrub-public-history.sh --execute                     archive, then rewrite local history
#   ./scrub-public-history.sh --execute --force-push        rewrite, then force-push dev

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ORIGIN_URL="https://github.com/extrode/nativeweb.git"
DEFAULT_ARCHIVE_DIR="$(cd "$REPO_ROOT/.." && pwd)/nativeweb-history-archive"

EXECUTE=0
FORCE_PUSH=0
ARCHIVE_DIR="$DEFAULT_ARCHIVE_DIR"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --execute|-e) EXECUTE=1; shift ;;
    --force-push) FORCE_PUSH=1; shift ;;
    --archive-dir) ARCHIVE_DIR="$2"; shift 2 ;;
    *) echo "unknown flag: $1" >&2; exit 1 ;;
  esac
done

ok()    { printf 'ok:    %s\n' "$1"; }
skip()  { printf 'skip:  %s\n' "$1"; }
doing() { printf 'do:    %s\n' "$1"; }
dry()   { printf 'dry:   %s\n' "$1"; }
bad()   { printf 'FAIL:  %s\n' "$1" >&2; }

cd "$REPO_ROOT"

# The git-filter-repo.exe wrapper on PATH can produce no stdout/stderr in some shells and can
# exit nonzero on valid input (same caveat JauntyQ's own script notes); invoke the underlying
# Python module directly so its diagnostics are actually visible.
FILTER_REPO_PY="$(python3 -c 'import git_filter_repo; print(git_filter_repo.__file__)')"
git_filter_repo() { python3 "$FILTER_REPO_PY" "$@"; }

if [[ -n "$(git status --porcelain)" ]]; then
  bad "working tree is not clean; commit or stash before running this"
  exit 1
fi

IDP_PATHS="$HERE/scrub-paths-idp.txt"
DEVMARKS_PATHS="$HERE/scrub-paths-devmarks.txt"
TEXT_RULES="$HERE/scrub-text-rules.txt"
MESSAGE_RULES="$HERE/scrub-message-rules.txt"

# --- Phase 1: build and verify the archive (always runs, read-only against this repo) --------

build_archive() {
  local name="$1" paths_file="$2"
  local bundle="$ARCHIVE_DIR/nativeweb-${name}-history.bundle"
  local scratch="$REPO_ROOT/tmp/repo-split-archive-${name}"

  if [[ -f "$bundle" ]] && git bundle verify "$bundle" >/dev/null 2>&1; then
    skip "$bundle already exists and verifies; not rebuilding"
    return 0
  fi

  doing "building ${name} archive (full history, not a snapshot) at $bundle"
  rm -rf "$scratch"
  mkdir -p "$ARCHIVE_DIR" "$(dirname "$scratch")"
  git clone --no-hardlinks --quiet "$REPO_ROOT" "$scratch"
  (
    cd "$scratch"
    rm -f .git/filter-repo/already_ran
    git_filter_repo --force --paths-from-file "$paths_file"
  )
  git -C "$scratch" bundle create "$bundle" --all
  rm -rf "$scratch"

  if ! git bundle verify "$bundle" >/dev/null 2>&1; then
    bad "archive bundle failed to verify: $bundle"
    exit 1
  fi
  ok "archive written and verified: $bundle"
}

build_archive "idp" "$IDP_PATHS"
build_archive "devmarks" "$DEVMARKS_PATHS"

if [[ "$EXECUTE" -eq 0 ]]; then
  dry "previewing rewrite (git filter-repo --dry-run); no changes will be made to this repo"
  ALREADY_RAN="$REPO_ROOT/.git/filter-repo/already_ran"
  rm -f "$ALREADY_RAN"
  # --force is required even for --dry-run: filter-repo refuses to run at all unless the repo is
  # a fresh clone or --force is given.
  git_filter_repo --force --dry-run \
    --paths-from-file "$IDP_PATHS" \
    --paths-from-file "$DEVMARKS_PATHS" \
    --invert-paths \
    --replace-text "$TEXT_RULES" \
    --replace-message "$MESSAGE_RULES"
  dry "review .git/filter-repo/ for the analysis; re-run with --execute to apply"
  exit 0
fi

# --- Phase 2: the real rewrite (only past here with --execute) --------------------------------

for name in idp devmarks; do
  bundle="$ARCHIVE_DIR/nativeweb-${name}-history.bundle"
  if [[ ! -f "$bundle" ]] || ! git bundle verify "$bundle" >/dev/null 2>&1; then
    bad "refusing to rewrite: $bundle is missing or does not verify"
    bad "the archive must exist and verify before the real repo is touched"
    exit 1
  fi
done
ok "both archive bundles present and verified in $ARCHIVE_DIR"

CURRENT_DEV_SHA="$(git rev-parse dev)"
ok "dev is at $CURRENT_DEV_SHA before rewrite (used to pin the force-push lease below)"

mkdir -p "$REPO_ROOT/tmp"
BACKUP="$REPO_ROOT/tmp/pre-scrub-backup-$(date +%Y%m%dT%H%M%S).bundle"
doing "backing up full history to $BACKUP before rewriting"
git bundle create "$BACKUP" --all
ok "backup written: $BACKUP"

doing "rewriting history (git filter-repo --force)"
ALREADY_RAN="$REPO_ROOT/.git/filter-repo/already_ran"
rm -f "$ALREADY_RAN"
git_filter_repo --force \
  --paths-from-file "$IDP_PATHS" \
  --paths-from-file "$DEVMARKS_PATHS" \
  --invert-paths \
  --replace-text "$TEXT_RULES" \
  --replace-message "$MESSAGE_RULES"
ok "history rewritten"

doing "re-adding origin remote (filter-repo removes it as a safety measure)"
git remote remove origin 2>/dev/null || true
git remote add origin "$ORIGIN_URL"
ok "origin set to $ORIGIN_URL"

if [[ "$FORCE_PUSH" -eq 0 ]]; then
  skip "not pushing (pass --force-push to force-push dev over origin)"
  exit 0
fi

doing "force-pushing dev to origin (lease pinned to the pre-rewrite tip captured above)"
git push origin dev "--force-with-lease=dev:${CURRENT_DEV_SHA}"
ok "pushed"
