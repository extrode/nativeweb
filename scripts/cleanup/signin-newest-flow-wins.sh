#!/usr/bin/env bash
# signin-newest-flow-wins.sh -- written 2026-11-03 after plan 015 (sign-in newest flow wins)
# and its review were merged into dev.
#
# 1. Remove the three worker worktrees the work left under .worktrees/. git worktree remove
#    refuses a dirty tree, and that refusal is left alone.
# 2. Delete the work's local branches. git branch -d refuses any not merged into dev.
#
# Usage, from the repository root, standing on dev:
#   scripts/cleanup/signin-newest-flow-wins.sh             # dry run, changes nothing
#   scripts/cleanup/signin-newest-flow-wins.sh --execute   # act
set -euo pipefail

execute=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        *) printf 'unknown argument: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0
mode="dry run"
[ "$execute" -eq 1 ] && mode="execute"
printf '== signin-newest-flow-wins: %s [%s] ==\n' "$repo" "$mode"

branch="$(git branch --show-current)"
if [ "$branch" != "dev" ]; then
    printf 'refuse: standing on "%s". git branch -d judges merged-ness against the current\n' "${branch:-detached HEAD}"
    printf '        branch, so run this from dev: git switch dev\n'
    [ "$execute" -eq 1 ] && exit 1
    exit 0
fi

workers=(worker-nw-signin-015 worker-nw-signin-rv1 worker-nw-signin-rv1b)

printf '\n-- 1. remove worker worktrees --\n'
for name in "${workers[@]}"; do
    dir="$repo/.worktrees/$name"
    if ! git worktree list --porcelain | grep -qxF "worktree $dir"; then
        printf 'skip:  worktree %s is not registered\n' "$dir"
        continue
    fi
    if [ "$execute" -eq 1 ]; then
        if git worktree remove "$dir"; then
            printf 'git worktree remove %s\n' "$dir"
        else
            printf 'refused: %s is dirty or locked. Left alone.\n' "$dir"
            failed=1
        fi
    else
        printf 'would: git worktree remove %s\n' "$dir"
    fi
done
[ "$execute" -eq 1 ] && git worktree prune

printf '\n-- 2. delete merged branches --\n'
branches=(
    chore/worker-nw-signin-015
    chore/worker-nw-signin-rv1
    chore/worker-nw-signin-rv1b
    fix/signin-newest-flow-wins
    fix/signin-015-review
    chore/signin-015-cleanup-script
)
for target in "${branches[@]}"; do
    if ! git rev-parse --verify --quiet "refs/heads/$target" >/dev/null; then
        printf 'skip:  branch %s no longer exists\n' "$target"
        continue
    fi
    if [ "$execute" -eq 1 ]; then
        if git branch -d "$target" >/dev/null 2>&1; then
            printf 'git branch -d %s\n' "$target"
        else
            printf 'refused: %s is not merged into dev, or still checked out in a worktree. Left alone.\n' "$target"
            failed=1
        fi
    else
        printf 'would: git branch -d %s\n' "$target"
    fi
done

printf '\n-- remaining --\n'
git worktree list
git branch --list 'chore/worker-nw-signin-*' 'fix/signin-newest-flow-wins' 'fix/signin-015-review' 'chore/signin-015-cleanup-script'

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
