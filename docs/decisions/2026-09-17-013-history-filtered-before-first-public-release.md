# 013 — the history was filtered before the first public release

Date: 2026-09-17
Status: accepted

## Context

NativeWeb is going public, following Jaunty and JauntyQ. Both of those repositories filtered
their history before the first public push rather than after
(`docs/decisions/2026-08-31-008-history-rewritten-before-first-public-release.md` in jaunty,
`docs/decisions/2026-09-02-001-history-filtered-before-first-public-release.md` in jauntyq) —
"a rewrite recalls nothing that has already been cloned... there is exactly one moment at which
this can be done."

A full scan of this repository's history (every blob, via `tools/audit/redact.ts`'s own rules;
every commit message; every ever-tracked path) found no leaked credential, but did find two
products' worth of source code reachable in history after being removed from HEAD, and one
identity leak in commit messages.

## Decision

The public repository carries NativeWeb's real history, with the authorship and dates it was
written with. Before the first push the history was filtered with `git filter-repo`
(`scripts/repo-split/scrub-public-history.sh` / `.ps1`) to:

- **Remove the identity provider's source**, reachable in every commit before its removal at
  `f743436` (`docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`). This was never framework
  code — WebAuthn policy, TOTP secret handling, opaque-token hashing and realm/SSO logic for a
  live product, epass.io.
- **Remove DevMarks's source**, a bookmarks product spun out to manzil.my and removed from HEAD
  at `0760c7c`, the same pattern at smaller scale.
- **Replace two real infrastructure IPs** with RFC 5737 documentation-range placeholders.
- **Replace the pre-Extrode org identity, "extrode-legacy-org"**, in commit messages and in tracked file
  content (`--replace-message` and `--replace-text` respectively — the same string, two fast-export
  sections neither flag alone reaches). Author and committer identity (the maintainer's own
  verified name and email) is untouched, matching JauntyQ's own carve-out; it is not a leak.
- **Rename the two `scripts/cleanup/` scripts whose own filename names "extrode-legacy-org"**
  (`relocate-nativeweb-to-extrode-legacy-org.ps1`, `stale-extrode-legacy-org-references.ps1`) to
  `relocate-nativeweb-to-legacy-org.ps1` and `stale-legacy-org-references.ps1`, via
  `--path-rename`. `--replace-text` rewrites blob content, never a path, so these two would
  otherwise still name the org in a plain `git log`/`ls` of the public repo after every other
  transform ran. Their content is left alone by this rename — it is already covered by the
  `--replace-text` pass above, and rewriting it by hand pre-emptively was rejected once already,
  in `chore/backlog-sweep` (2026-08-29): it would make each script contradict its own opening
  paragraph before the one moment this history rewrite is designed to do that correctly.

**Removed history was archived, not discarded.** `scripts/repo-split/scrub-public-history.sh`
builds a full commit-history bundle of each removed product's paths — `nativeweb-idp-history.bundle`
and `nativeweb-devmarks-history.bundle` — to a location **outside this repository's working
tree** before the real repo is touched, and refuses to run the real rewrite if that archive is
missing, stale, or fails to verify. The default is a fixed, documented sibling directory next to
the repo, not a secret one — "outside the repository" describes where the bundles live on disk,
not that the location is undisclosed. Overridable with `--archive-dir`/`-ArchiveDir`; the bundles
themselves are never committed to this repository under either the default or an overridden
location.

## Consequences

- Some files' histories begin at their first surviving revision rather than at their first
  revision. A `git log --follow` that ends earlier than expected on `apps/web`, the auth/data
  libraries, `libs/nativeweb.signin.epass*`, the pre-2026-08-01 `db/` schema tree, or
  `apps/devmarks` is this, not a lost file — the full history for each is in its archive bundle,
  held privately.
- **Commit SHAs cited in any document written before 2026-09-17 do not resolve.** Rewriting any
  commit changes every descendant, so the entire graph is renumbered.
- Two IP addresses that appeared in deployment documentation now read as `203.0.113.10` and
  `203.0.113.20` (RFC 5737) in every historical revision, not just the current one.
- A handful of commit messages that named the pre-Extrode org now read "extrode-legacy-org" in
  that spot instead.

## Why before rather than after

Once a repository is public, its history is public permanently, whatever is force-pushed over it
afterwards — so there is exactly one moment at which this can be done, and it is before the first
person clones it. Doing it in one pass rather than several also means one renumbering of the
graph instead of many.

## Why archived rather than simply discarded

The identity provider and DevMarks are both real, working implementations for products that are
not NativeWeb — but they are not nothing, either. `git filter-repo`'s own safety model already
assumes a rewrite might need to be walked back (the tool refuses to run twice, keeps a mapping of
old-to-new SHAs, and this script's own full-repo backup bundle exists for the same reason); the
per-product archive bundles extend that same caution to the two directories being removed
entirely rather than merely rewritten, in case either product's own repository (epass.io,
manzil.my) ever has a use for the commit-by-commit record of how it looked while it still lived
here.
