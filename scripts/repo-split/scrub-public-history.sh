#!/usr/bin/env bash
set -euo pipefail

# Filters NativeWeb's git history before it is pushed to a fresh public repository
# (docs/decisions/2026-10-03-015-fresh-public-repository.md, superseding 013).
#
# One git-filter-repo pass over the real repo:
#   - path removal:     paths-idp.txt, paths-devmarks.txt, paths-other.txt (--invert-paths)
#   - text replacement: text-rules.txt     (blob content)
#   - message rewrite:  message-rules.txt  (commit messages)
#   - path rename:      path-renames.txt   (files whose own name carries a removed string;
#                        --replace-text rewrites content, never a path)
#
# **The rules are not in this repository.** They spell out exactly what the public history must
# not contain, so they live in a private directory beside the history bundles, named by
# --rules-dir. Every rule's target is already absent from dev's tip before this runs, so the
# rewrite changes history only: the tip tree before and after must be identical, and
# audit-scrubbed-history.ts checks the result.
#
# Removed history is archived, not discarded: the IdP and DevMarks paths are bundled, with full
# history, to the archive directory before the real repo is touched, and the real rewrite refuses
# to run if either bundle is missing or fails to verify. The paths-other.txt files (hosting,
# sibling handovers, scrub material) are not bundled separately; the full pre-rewrite backup
# bundle written below and the private repository both hold them.
#
# Usage:
#   ./scrub-public-history.sh                      dry run + build/verify the archive
#   ./scrub-public-history.sh --execute            archive, then rewrite local history
#   --rules-dir <path>    the private rules (default: ../nativeweb-history-archive/rules)
#   --archive-dir <path>  where the bundles go (default: ../nativeweb-history-archive)
#
# It never pushes. After --execute it prints the commands that publish dev to the new repository.

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ORIGIN_URL="https://github.com/extrode/nativeweb.git"
DEFAULT_ARCHIVE_DIR="$(cd "$REPO_ROOT/.." && pwd)/nativeweb-history-archive"

# A machine-wide reference-transaction hook (dotfiles/githooks) refuses any ref move that is not
# a fast-forward, as general protection against an accidental force-reset. Every ref move this
# script makes is exactly that, deliberately -- filter-repo rewrites every commit and moves every
# branch/tag to point at its rewritten counterpart. Opt out for this script's own git commands
# only; it is not set globally and does not survive the shell that ran it.
export GIT_ALLOW_PROTECTED=1

EXECUTE=0
ARCHIVE_DIR="$DEFAULT_ARCHIVE_DIR"
RULES_DIR="$DEFAULT_ARCHIVE_DIR/rules"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --execute|-e) EXECUTE=1; shift ;;
    --archive-dir) ARCHIVE_DIR="$2"; shift 2 ;;
    --rules-dir) RULES_DIR="$2"; shift 2 ;;
    *) echo "unknown flag: $1" >&2; exit 1 ;;
  esac
done

ok()    { printf 'ok:    %s\n' "$1"; }
skip()  { printf 'skip:  %s\n' "$1"; }
doing() { printf 'do:    %s\n' "$1"; }
dry()   { printf 'dry:   %s\n' "$1"; }
bad()   { printf 'FAIL:  %s\n' "$1" >&2; }

# Both resolved to absolute now, before any `cd`: a relative path would otherwise resolve against
# the scratch clone's own directory inside build_archive, then get deleted along with it.
if [[ ! -d "$RULES_DIR" ]]; then
  bad "rules directory not found: $RULES_DIR (pass --rules-dir)"
  exit 1
fi
RULES_DIR="$(cd "$RULES_DIR" && pwd)"
for f in paths-idp.txt paths-devmarks.txt paths-other.txt text-rules.txt message-rules.txt path-renames.txt; do
  if [[ ! -f "$RULES_DIR/$f" ]]; then
    bad "rules directory is missing $f: $RULES_DIR"
    exit 1
  fi
done
mkdir -p "$ARCHIVE_DIR"
ARCHIVE_DIR="$(cd "$ARCHIVE_DIR" && pwd)"

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

IDP_PATHS="$RULES_DIR/paths-idp.txt"
DEVMARKS_PATHS="$RULES_DIR/paths-devmarks.txt"
OTHER_PATHS="$RULES_DIR/paths-other.txt"
TEXT_RULES="$RULES_DIR/text-rules.txt"
MESSAGE_RULES="$RULES_DIR/message-rules.txt"

# --path-rename is not a selection filter (unlike --paths-from-file), so passing these alongside
# --invert-paths over an unrelated path list is safe -- verified in a scratch repo: the renamed
# file survives and lands under its new name while the --invert-paths exclusions are still
# removed. Never put these in a --paths-from-file: that flag also acts as a --path filter, and
# combined with --invert-paths would delete exactly the files this is meant to keep.
# path-renames.txt holds one `old:new` per line; `#` lines and blank lines are skipped.
PATH_RENAMES=()
while IFS= read -r line; do
  line="${line%$'\r'}"
  [[ -z "$line" || "$line" == \#* ]] && continue
  PATH_RENAMES+=(--path-rename "$line")
done < "$RULES_DIR/path-renames.txt"

FILTER_ARGS=(
  --paths-from-file "$IDP_PATHS"
  --paths-from-file "$DEVMARKS_PATHS"
  --paths-from-file "$OTHER_PATHS"
  --invert-paths
  --replace-text "$TEXT_RULES"
  --replace-message "$MESSAGE_RULES"
  "${PATH_RENAMES[@]}"
)

# --- Phase 1: build and verify the archive (always runs, read-only against this repo) --------

build_archive() {
  local name="$1" paths_file="$2"
  local bundle="$ARCHIVE_DIR/nativeweb-${name}-history.bundle"
  local scratch="$REPO_ROOT/tmp/repo-split-archive-${name}"

  # `git bundle verify` proves the bundle is intact, not that it is current: a bundle built
  # before a later commit landed on dev would verify cleanly and be reused, silently missing
  # that commit's changes under the archived paths. Compare its recorded dev tip to the real one.
  if [[ -f "$bundle" ]] && git bundle verify "$bundle" >/dev/null 2>&1; then
    local bundle_dev_sha
    bundle_dev_sha="$(git bundle list-heads "$bundle" refs/heads/dev 2>/dev/null | awk '{print $1}')"
    if [[ -n "$bundle_dev_sha" && "$bundle_dev_sha" == "$(git rev-parse dev)" ]]; then
      skip "$bundle already exists, verifies, and matches dev's current tip; not rebuilding"
      return 0
    fi
    doing "existing $bundle is stale (built from a different dev tip); rebuilding"
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
  rm -f "$REPO_ROOT/.git/filter-repo/already_ran"
  # --force is required even for --dry-run: filter-repo refuses to run at all unless the repo is
  # a fresh clone or --force is given.
  git_filter_repo --force --dry-run "${FILTER_ARGS[@]}"
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

# The archive and the backup are built from local heads, so origin must not hold a dev commit
# this checkout lacks. Local dev ahead of origin is the normal shape (merged, not yet pushed).
# Other refs on origin no longer matter: the push target is a new repository, and the old one is
# kept private.
LOCAL_DEV_SHA="$(git rev-parse dev)"
if git remote get-url origin >/dev/null 2>&1; then
  doing "checking origin's dev is an ancestor of local dev"
  REMOTE_REFS="$(git ls-remote origin 2>&1)" || { bad "could not reach origin: $REMOTE_REFS"; exit 1; }
  REMOTE_DEV_SHA="$(printf '%s\n' "$REMOTE_REFS" | awk '$2 == "refs/heads/dev" {print $1}')"
  if [[ -n "$REMOTE_DEV_SHA" ]]; then
    git fetch --quiet origin refs/heads/dev 2>/dev/null || true
    if ! git merge-base --is-ancestor "$REMOTE_DEV_SHA" "$LOCAL_DEV_SHA" 2>/dev/null; then
      bad "origin/dev ($REMOTE_DEV_SHA) is not an ancestor of local dev ($LOCAL_DEV_SHA)"
      bad "origin has a commit this checkout does not; fetch and reconcile before rewriting --"
      bad "the archive is built from local heads and would silently miss it"
      exit 1
    fi
  fi
  ok "origin's dev is an ancestor of local dev ($LOCAL_DEV_SHA)"
fi

mkdir -p "$REPO_ROOT/tmp"
STAMP="$(date +%Y%m%dT%H%M%S)"
BACKUP="$REPO_ROOT/tmp/pre-scrub-backup-$STAMP.bundle"
doing "backing up full history to $BACKUP before rewriting"
git bundle create "$BACKUP" --all
ok "backup written: $BACKUP"

SHA_RECORD="$REPO_ROOT/tmp/pre-scrub-dev-sha-$STAMP.txt"
echo "$LOCAL_DEV_SHA" > "$SHA_RECORD"
ok "pre-rewrite dev SHA recorded at $SHA_RECORD"

# Compared by tree hash, not by `git diff <old-sha>`: filter-repo prunes the old commits, so the
# old SHA no longer resolves afterwards. Equal tree hashes mean byte-identical tip trees.
OLD_TREE="$(git rev-parse 'dev^{tree}')"

doing "rewriting history (git filter-repo --force)"
rm -f "$REPO_ROOT/.git/filter-repo/already_ran"
git_filter_repo --force "${FILTER_ARGS[@]}"
ok "history rewritten"

NEW_TREE="$(git rev-parse 'dev^{tree}')"
if [[ "$NEW_TREE" != "$OLD_TREE" ]]; then
  bad "dev's tip tree changed ($OLD_TREE -> $NEW_TREE): a rule matched something still at the tip"
  bad "the rewrite is not history-only; restore from $BACKUP and clean the tip first"
  exit 1
fi
ok "dev's tip tree is unchanged ($NEW_TREE)"

# .git/filter-repo/ keeps the analysis and, from any earlier dry run, a full unfiltered
# fast-export. Nothing pushes it, but it is the old history in plain text on disk.
rm -rf "$REPO_ROOT/.git/filter-repo"
ok "removed .git/filter-repo/"

doing "re-adding origin remote (filter-repo removes it as a safety measure)"
git remote remove origin 2>/dev/null || true
git remote add origin "$ORIGIN_URL"
ok "origin set to $ORIGIN_URL"

cat <<EOF

Next, in order:
  1. audit:   deno run -A scripts/repo-split/audit-scrubbed-history.ts --rules-dir "$RULES_DIR"
  2. build and test the rewritten tree
  3. publish, once the old repository is renamed and the new one exists:
       gh repo rename nativeweb-private -R extrode/nativeweb
       gh repo create extrode/nativeweb --public
       git push origin dev
EOF
