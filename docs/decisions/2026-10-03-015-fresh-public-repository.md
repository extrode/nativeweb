# 015 — NativeWeb goes public as a fresh repository with filtered history

Date: 2026-10-03
Status: accepted. Supersedes `2026-09-17-013-history-filtered-before-first-public-release.md`.
Plan: `docs/plans/2026-10-03-014-publish-nativeweb-as-a-fresh-public-repository-with-filtered.md`

## Context

NativeWeb is a framework anyone can use, and it is going public. Decision 013 planned to filter
this repository's history in place and force-push it. That rewrite never ran, and by 2026-10-03
its remove list had fallen behind: about 140 removed-from-HEAD paths belonging to the departed
identity provider were not on it, its commit-message rules covered only one string, and its rule
files spelled out every string they removed, so they could not ship.

Flipping the existing repository to public has two costs a rewrite cannot undo: every Actions run
log becomes readable, and commits overwritten by a force-push stay reachable by SHA on GitHub
until GitHub Support purges them.

## Decision

- **A new public repository receives the filtered `dev`.** The existing private repository is
  renamed and kept as the backup; the new one takes the old name, so old links reach it. Only
  `dev` is pushed: no tags, no other branches.
- **History is kept, minus what is not ours to publish.** `git filter-repo` removes the identity
  provider's and DevMarks's source from every commit, plus files that only described private
  infrastructure, and replaces infrastructure specifics and the pre-Extrode org name in file
  content and commit messages. Author and committer identity is untouched.
- **HEAD is cleaned first, by ordinary reviewed commits.** Every path the rewrite removes and every
  string it replaces is already absent from `dev`'s tip, so the rewrite changes history only. The
  scrub script records the tip's tree hash before the rewrite and fails if it differs after.
- **The rule files live outside the repository**, beside the private history archive.
  `scripts/repo-split/scrub-public-history.sh` takes `--rules-dir` and refuses to run without it,
  and `scripts/repo-split/audit-scrubbed-history.ts` reads the same files and exits 1 on any path,
  blob or commit message that still matches.
- **Security reports go through GitHub private vulnerability reporting** (`SECURITY.md`).
- **CI moves to GitHub-hosted runners.** The self-hosted runners of decision 014 are retired
  before the push: a public repository's workflows can be triggered by outside pull requests.

## Consequences

- Commit SHAs cited in documents written before the rewrite do not resolve in the public
  repository.
- A `git log --follow` that stops early on a file that once shared a directory with the identity
  provider is this filter, not a lost file. The removed history is held in private archive
  bundles.
- Historical documents name the pre-Extrode org as `extrode-legacy-org`, and describe
  infrastructure in generic terms.
- The audit script is the check for any future publish: it fails on today's private history and
  must exit 0 on the public one.
