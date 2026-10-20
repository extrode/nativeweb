#!/usr/bin/env bash
# public-release-leftovers.sh -- written 2026-10-20 by the public-release docs sweep.
#
# 1. git rm four tracked files nothing uses: a one-off extraction script whose work is done, a
#    page renderer no route calls, and two leftover copies of boot.js (the layouts load
#    /_nativeweb/boot.js, delivered by nativeweb.web). Each is checked for references at run time.
#    The deletions are staged, not committed: build, test, then commit them yourself.
# 2. Delete the local branches this work merged. git branch -d refuses any that are not merged
#    into the branch you are standing on, and that refusal is left alone.
#
# Usage, from the repository root on a branch that is not main or dev:
#   scripts/cleanup/public-release-leftovers.sh                     # dry run, changes nothing
#   scripts/cleanup/public-release-leftovers.sh --execute           # section 2 only
#   scripts/cleanup/public-release-leftovers.sh --execute --git-rm  # sections 1 and 2
set -euo pipefail

execute=0
git_rm=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        --git-rm) git_rm=1 ;;
        *) printf 'unknown argument: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0
mode="dry run"
[ "$execute" -eq 1 ] && mode="execute"
printf '== public-release-leftovers: %s [%s] ==\n' "$repo" "$mode"

branch="$(git branch --show-current)"
if [ "$branch" = "main" ] || [ "$branch" = "dev" ] || [ -z "$branch" ]; then
    printf 'refuse: standing on "%s". Switch to a work branch off dev first:\n' "${branch:-detached HEAD}"
    printf '        git switch -c chore/public-release-leftovers dev\n'
    [ "$execute" -eq 1 ] && exit 1
    exit 0
fi

# path|text whose appearance elsewhere would mean the file is still used
targets=(
    "scripts/extract-manzil.ps1|extract-manzil.ps1"
    "apps/sample/Render/ArticlesPage.cs|ArticlesPage"
    "apps/sample/wwwroot/vendor/nativeweb/boot.js|vendor/nativeweb/boot.js"
    "skeleton/apps/__product__/wwwroot/vendor/nativeweb/boot.js|vendor/nativeweb/boot.js"
)

printf '\n-- 1. git rm unused tracked files --\n'
for entry in "${targets[@]}"; do
    rel="${entry%%|*}"
    symbol="${entry#*|}"
    if ! git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1; then
        printf 'skip:  %s is not tracked (already removed)\n' "$rel"
        continue
    fi
    # Live code only. Specs, plans, decisions, the changelog, work/ and cleanup scripts are
    # records of what happened and are expected to name it.
    refs="$(git grep -l -F "$symbol" -- '*.cs' '*.csproj' '*.html' '*.ps1' '*.sh' '*.ts' '*.yml' '*.props' '*.targets' \
        ':!docs' ':!work' ':!CHANGELOG.md' ':!scripts/cleanup' ":!$rel" || true)"
    if [ -n "$refs" ]; then
        printf 'note:  %s is named by:\n%s\n' "$rel" "$(printf '%s\n' "$refs" | sed 's/^/         /')"
        printf '       check those are comments, not uses, before committing.\n'
    fi
    if [ "$execute" -eq 1 ] && [ "$git_rm" -eq 1 ]; then
        git rm -q -- "$rel"
        printf 'git rm %s\n' "$rel"
    else
        printf 'would: git rm %s\n' "$rel"
    fi
done

printf '\n-- 2. delete merged branches --\n'
branches=(
    chore/isl-netc-1.2
    docs/stale-docs-sweep
    chore/worker-nw-docs-a
    chore/worker-nw-docs-b
    chore/worker-nw-docs-c
    chore/worker-nw-docs-d
    chore/worker-nw-ci
    chore/worker-nw-pkg
)
git worktree prune
for target in "${branches[@]}"; do
    if ! git rev-parse --verify --quiet "refs/heads/$target" >/dev/null; then
        printf 'skip:  branch %s no longer exists\n' "$target"
        continue
    fi
    if [ "$execute" -eq 1 ]; then
        if git branch -d "$target" >/dev/null 2>&1; then
            printf 'git branch -d %s\n' "$target"
        else
            printf 'refused: %s is not merged into %s. Left alone.\n' "$target" "$branch"
            failed=1
        fi
    else
        printf 'would: git branch -d %s\n' "$target"
    fi
done

printf '\n-- remaining --\n'
git status --short -- "${targets[@]%%|*}" 2>/dev/null || true
git branch --list 'chore/worker-nw-*' 'docs/stale-docs-sweep' 'chore/isl-netc-1.2'
if [ "$execute" -eq 1 ] && [ "$git_rm" -eq 1 ]; then
    printf '\nNext: dotnet build && dotnet test --project tests/nativeweb.repo.tests.unit, then commit.\n'
fi

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
