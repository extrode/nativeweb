#!/usr/bin/env bash
# release-0.1.0-scratch.sh -- written 2026-11-04 by the session that scrubbed plan 015 from the
# public history, fixed the local test database and released 0.1.0.
#
# Deletes this Mac's scratch under tmp/. Three groups, each behind its own flag:
#   1. scratch            logs, review diffs, the rewritten scrub clone and its bundle
#   2. scrub rules        rule files naming the scrubbed text; they belong outside the repo
#   3. pre-scrub backups  bundles and copies holding history from before the scrub
# Left alone: tmp/scaffold-e2e, tmp/signin-e2e and tmp/sample-publish.lock, which the e2e
# fixtures create and reuse.
#
# Every target is checked at run time: a path git tracks is refused, an absent one is a skip, and
# a nested repository is refused if any branch head in it is not already on origin/dev.
#
# Usage, from the repository root:
#   scripts/cleanup/release-0.1.0-scratch.sh                                  # dry run
#   scripts/cleanup/release-0.1.0-scratch.sh -e --delete-scratch              # group 1
#   scripts/cleanup/release-0.1.0-scratch.sh -e --delete-scrub-rules          # group 2
#   scripts/cleanup/release-0.1.0-scratch.sh -e --delete-pre-scrub-backups    # group 3
set -euo pipefail

execute=0
delete_scratch=0
delete_rules=0
delete_backups=0
for arg in "$@"; do
    case "$arg" in
        --execute|-e) execute=1 ;;
        --delete-scratch) delete_scratch=1 ;;
        --delete-scrub-rules) delete_rules=1 ;;
        --delete-pre-scrub-backups) delete_backups=1 ;;
        *) printf 'unknown argument: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0
mode="dry run"
[ "$execute" -eq 1 ] && mode="execute"
printf '== release-0.1.0-scratch: %s [%s] ==\n' "$repo" "$mode"

if ! git remote get-url origin 2>/dev/null | grep -q 'extrode/nativeweb'; then
    printf 'refuse: origin is not extrode/nativeweb. Run this from the nativeweb checkout.\n'
    [ "$execute" -eq 1 ] && exit 1
    exit 0
fi
git fetch -q origin dev || { printf 'refuse: cannot fetch origin/dev.\n'; [ "$execute" -eq 1 ] && exit 1; exit 0; }

# remove <flag-on> <rel>
remove() {
    local on="$1" rel="$2"
    if [ ! -e "$rel" ]; then
        printf 'skip:  %s is already gone\n' "$rel"
        return
    fi
    if git ls-files --error-unmatch -- "$rel" >/dev/null 2>&1 \
        || [ -n "$(git ls-files -- "$rel")" ]; then
        printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"
        failed=1
        return
    fi
    if [ -d "$rel" ] && [ "$(git -C "$rel" rev-parse --show-toplevel 2>/dev/null)" = "$repo/$rel" ]; then
        local head
        for head in $(git -C "$rel" for-each-ref --format='%(objectname)' refs/heads); do
            if ! git merge-base --is-ancestor "$head" origin/dev 2>/dev/null; then
                printf 'refuse: %s has a branch at %s that is not on origin/dev. Left alone.\n' "$rel" "${head:0:8}"
                failed=1
                return
            fi
        done
    fi
    if [ "$execute" -eq 1 ] && [ "$on" -eq 1 ]; then
        rm -rf -- "$rel"
        printf 'rm -rf %s\n' "$rel"
    else
        printf 'would: rm -rf %s\n' "$rel"
    fi
}

printf '\n-- 1. scratch (--delete-scratch) --\n'
for rel in \
    tmp/build-docs.log tmp/pack-release.log \
    tmp/full-test.log tmp/full-test-2.log tmp/full-test-3.log tmp/full-test-4.log \
    tmp/scaffold-int.log tmp/scaffold-int-1.log tmp/scaffold-int-2.log tmp/scaffold-int-3.log \
    tmp/t-nativeweb.repo.tests.unit.log tmp/t-nativeweb.scaffold.tests.integration.log tmp/t-repo.log \
    tmp/review-fix-branch.diff tmp/review-range.diff tmp/rv-lib.diff tmp/rv-tests.diff \
    tmp/review-signin-015-criteria.md tmp/readme-preview \
    tmp/scrub-clone tmp/dev-rewritten.bundle; do
    remove "$delete_scratch" "$rel"
done

printf '\n-- 2. scrub rules (--delete-scrub-rules) --\n'
for rel in tmp/scrub-rules tmp/scrub-round-2; do
    remove "$delete_rules" "$rel"
done

printf '\n-- 3. pre-scrub backups (--delete-pre-scrub-backups) --\n'
# dev-unpushed.bundle holds dev at 455f6798, before plan 015 was scrubbed; tmp/readme holds the
# 2026-10-20 README work, including two bundles cut before that scrub finished.
for rel in tmp/dev-unpushed.bundle tmp/readme; do
    remove "$delete_backups" "$rel"
done

printf '\n-- remaining in tmp/ --\n'
ls -1 tmp 2>/dev/null || true

if [ "$execute" -eq 1 ] && [ "$failed" -eq 1 ]; then exit 1; fi
exit 0
