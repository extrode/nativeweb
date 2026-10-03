# Publish NativeWeb as a fresh public repository, with filtered history

## Context

NativeWeb goes public (it is a framework anyone can use). The user wants its real history
preserved, minus anything they do not want public. Decisions taken 2026-10-03:

- **D1, fresh repository.** The current private `extrode/nativeweb` is renamed to
  `extrode/nativeweb-private` and kept as a backup; a new public `extrode/nativeweb` receives the
  filtered `dev`. Flipping the old repo in place would expose 80 Actions run logs and leave the
  pre-rewrite commits viewable by SHA until GitHub Support purges them.
- **D2, identity.** All 1892 commits carry one author and committer identity, the user's own name
  and address (`git log --all --format='%an <%ae>|%cn <%ce>' | sort -u` gives one line). It stays
  as is. No mailmap.
- **D2b, the legacy company name.** The user's surname is also the name of their former company.
  **The person's name may appear; the company may not.** So the company forms are replaced in
  file content and commit messages: the org name, its GitHub org and package feed URLs, its domain
  in local checkout paths, `<Authors>` values. "Syed Beparey" as a person's name is left alone,
  as are the author/committer lines (D2). The rules are regexes with a negative lookbehind for the
  person's name, not blanket literals, so a surname in a blob is never rewritten. Every
  remaining hit is listed in the inventory for review.
- **D3, hosting.** **No trace of where or how NativeWeb is hosted**: provider, host names and
  baselines, region, private subnets, co-tenancy (which products share a host), the private backup
  setup, public IPs. Sibling product names (epass, ghosthive, justsmtp and the rest) go wherever they
  describe hosting; every other hit is listed in the inventory for the user to keep or cut. The
  `AGENTS.md` scope section's epass.io rationale (the "Django line") stays.
- **D4, security contact.** `SECURITY.md` points at GitHub private vulnerability reporting.

Starting point: the scrub tooling from 2026-09-17 (`scripts/repo-split/`, decision 013) exists
but was never executed. Against every path ever tracked (2239), its remove list misses 213
removed-from-HEAD paths, about 140 of them the IdP's (three test projects, its mail senders, its
sign-in sweeper) plus one DevMarks project. Its text rules miss the host's IPv6 address; its
message rules cover only the company name, while at least 30 commit messages name hosts, host
branches or the backup tool. And the rule files list the literals being removed, so they cannot
ship.

## Approach

Two invariants make the rewrite checkable:

1. **HEAD is cleaned first, by ordinary reviewed commits.** Every path the rewrite removes or
   renames, and every string it replaces, is already gone from `dev`'s tip. The rewrite then
   changes history only, and **the tip tree before and after must be identical** (`git diff
   --stat <old-tip> <new-tip>` empty). Fable verified this holds for `--invert-paths`,
   `--replace-text`, `--replace-message`, `--path-rename` and empty-commit pruning when their
   targets are absent at HEAD.
2. **Nothing that names a removed string ships.** The rule lists move out of the repository to
   `../nativeweb-history-archive/rules/`, beside the history bundles. The script takes
   `--rules-dir` and refuses if it is missing. An audit script reads the same lists and fails on
   any hit.

Removal uses a remove list, not a keep list: filter-repo does not follow renames, and the project
was named epass until the 2026-07-26 rename to `nativeweb.*`, so a keep list built from today's
paths would cut every framework file's history at that rename.

## Phase A: inventory (me, read-only). Gate: user reviews it

`tmp/public-release-inventory.md`, three tables:

- **Paths to remove from all history**, grouped with reason and count: the existing IdP and
  DevMarks lists; from the 213 uncovered, the IdP test projects
  `tests/nativeweb.web.tests.{unit,integration,e2e}`, `libs/epass.mail`, `libs/nativeweb.mail`,
  `libs/nativeweb.signin` and its tests, `libs/nativeweb.devmarks.data` (kept: `libs/epass.security`,
  the `nativeweb.security` lineage, plus skeleton, docs, sample, assets); hosting-specific files
  (decisions 005 and 006, the two backup handovers, the edge-sidecar transport plan, the backup,
  DR and edge cleanup scripts); sibling-product handovers; the scrub rule files and the
  2026-09-17 scrub plan, which quote removed literals.
- **Strings to replace**, each with its history-wide count in blobs **and** in messages, and its
  replacement: company forms (D2b), the two public IPv4 and one IPv6, the private subnet, provider,
  host, build-host and region names, the dev and production host names (to `example.com` forms),
  the backup tool's name, host branch names that appear in merge subjects. "fleet" (491 hits) and
  "incus" (240) are judged per context, not replaced blindly.
- **HEAD files to edit** for invariant 1: remaining mentions (including the 45 files carrying the
  company name, the repo tests that assert on it such as `OrgReferenceTests.cs`, and the two
  `scripts/cleanup/` files whose names carry it, renamed at HEAD so the historical
  `--path-rename` is a no-op on the tip); doc links into removed files;
  `tests/nativeweb.render.tests.unit/FeedTests.cs`'s host fixture; host specifics baked into
  `scripts/deploy/` (the tooling stays, it is generic NativeAOT deploy tooling); the deployment
  reference and spec 011; `work/` status files; `CHANGELOG.md`; decision 013, which cites two
  pre-rewrite SHAs and describes a run that never happened; the generated `docs-site/`, which
  must be rebuilt after the doc edits.

The user marks keep/cut on the sibling-name, person-vs-company and fleet/incus rows. Nothing
past here starts until they have.

## Phase B: prep branch `chore/public-release-prep` (me)

1. **HEAD clean-up** per the approved inventory. Edits in place; regenerate `docs-site/` with
   `scripts/build-docs.sh`. Removals and renames go into `scripts/cleanup/public-release-prep.ps1`
   (dry run default, `-Execute` acts), which the user runs before I commit the result. It also
   removes the tracked `grep.exe.stackdump` (tidy only; it holds msys frame addresses, no leak)
   and `scrub-public-history.ps1` (see 2).
2. **Scrub script**, `scripts/repo-split/scrub-public-history.sh` only. The `.ps1` twin is
   retired: this is a one-shot run on this machine, and parity doubles every change.
   - `--rules-dir`, default `../nativeweb-history-archive/rules/` resolved from the repo root,
     refusing if absent. The rule files are copied there first; the in-repo copies leave through
     step 1's script.
   - the expanded remove list feeds the IdP archive bundle so the private archive stays complete.
     No separate hosting bundle: the full pre-rewrite backup bundle and `nativeweb-private` both
     hold that history already.
   - `--replace-message` gets every hosting literal, not only the company forms.
   - the origin pre-flight shrinks to the ancestor check (origin's `dev` must be an ancestor of
     local `dev`); the "origin has only dev" refusal and the `--force-push` flow are deleted. The
     push target is a new repository, and the old one survives as `nativeweb-private`.
   - after a real run it deletes `.git/filter-repo/`, which holds a full unfiltered export from
     the 2026-09-17 dry run, and prints the push commands for Phase E.
3. **Audit script** `scripts/repo-split/audit-scrubbed-history.ts` (Deno): given a repo and
   `--rules-dir`, walk every path, blob and commit message reachable from `dev`; exit 1 on any
   remove-list path or replace-list match; exit 1 on more than one author/committer identity;
   identity lines are excluded from the string check (D2). Print a per-path report of IdP and
   sibling markers (`WebAuthn`, `TwoFactor`, `OAuthToken`, `Consent`, `realm`, product names) as
   information. Tested with `deno test` against fixture repos in a `Deno.makeTempDir` directory
   each test removes.
4. **Docs**: rewrite the `AGENTS.md` JauntyQ license section and `README.md`'s "private feed only"
   paragraph for a public ISL-NETC repo on public JauntyQ packages; add `SECURITY.md`; decision
   `docs/decisions/2026-10-03-015-fresh-public-repository.md`, which supersedes 013 (013 gets
   `Status: superseded by 015` and its SHA citations removed); `CHANGELOG.md` Unreleased entry.
   None quotes a removed literal. This plan, promoted to `docs/plans/`, quotes none either.
5. Repo unit tests, `dotnet build` with 0 warnings, the audit's tests; each new test broken once
   and restored with `git checkout --`; the audit run against the current (unscrubbed) repo must
   fail, naming hits. Fable review, merge to `dev` `--no-ff`.

## Phase C: runner shutdown (user; any time before Phase E)

```
gh variable delete CI_LINUX_RUNNER -R extrode/nativeweb
gh variable delete CI_WINDOWS_RUNNER -R extrode/nativeweb
gh api repos/extrode/nativeweb/actions/runners --jq '.runners[]|[.id,.name]|@tsv'
gh api -X DELETE repos/extrode/nativeweb/actions/runners/<id>      # both
```

Then `docker compose down` in `scripts/ci-runner/`, and from an elevated shell
`C:\actions-runner\nativeweb\config.cmd remove`. I can run the first four on an explicit yes.

## Phase D: rewrite locally (me, after B is merged)

1. The local stash (`stash@{0}`, parked generated HTML) survives the rewrite only as its top
   entry. The user decides: keep it as a branch, or drop it.
2. Dry run on the real repo, which also rebuilds the archive bundles (the existing ones date from
   2026-09-17).
3. **Rehearsal**: `git clone --no-local` into `tmp/scrub-rehearsal/`, then `--execute` there with
   explicit `--rules-dir` and `--archive-dir` (the defaults resolve relative to the clone), then
   the audit. Report commits kept and pruned, files per top-level folder, the marker report. **The
   user reviews this before the real repo is touched.**
4. On the user's go: `--execute` on the real repo. It writes a full backup bundle and the
   pre-rewrite tip SHA to `tmp/` first.
5. Verify: the audit exits 0; `git diff --stat <old-tip> dev` is empty; `dotnet build` and the
   repo, render and forms suites pass.

Local only: the other branches and the `pre-idp-extraction` tag are rewritten with `dev` and
never pushed.

## Phase E: publish (user)

```
gh repo rename nativeweb-private -R extrode/nativeweb
gh repo create extrode/nativeweb --public --description "..."
git remote set-url origin https://github.com/extrode/nativeweb.git
git push origin dev
gh repo edit extrode/nativeweb --default-branch dev
```

`dev` only: no tags, no other branches. Creating the new repo under the old name replaces the
rename's redirect, so old links reach the public repo.

## Phase F: public repo settings (user, web UI)

- Actions: require approval for fork PR workflows from all outside contributors; default workflow
  permissions read-only.
- Secret scanning with push protection, private vulnerability reporting, Dependabot alerts.
- Ruleset on `dev`: no force-push, no deletion.
- Org secrets `CI_APP_ID` / `CI_APP_PRIVATE_KEY`: delete if nothing else uses them.

## Phase G: after it is live (me)

- Watch the first hosted CI run. **Known risk: `skeleton-stamp`** logs in to the org package feed
  with the job's `GITHUB_TOKEN`, whose package read access is per repository; the new repo is
  linked to no org package (no `NativeWeb.*` package exists; the JauntyQ and Jaunty ones link to
  their own repos). If restore gets 401/403, the fix is granting the new repo read access to the
  packages it needs, or checking whether the stamped config still needs that feed now that
  JauntyQ resolves from nuget.org. Fork PRs get a read-only token regardless.
- Close the runner line in `work/todo.md`; update memory; structure-audit.

## Verification summary

| Check | Proves |
|---|---|
| Audit fails on today's repo, exits 0 after the rewrite | it detects leaks, and none remain in paths, blobs or messages |
| `git diff --stat <old-tip> <new-tip>` empty | the rewrite changed history only |
| Build and repo, render, forms suites green after the rewrite | the shipped tree still works |
| Rehearsal reviewed before the real run | the user saw what goes public |
| Archive and backup bundles verify | removed history is held privately, not lost |
| First public CI run green | hosted runners work without the self-hosted ones |

## Confirm-first points

- Running `public-release-prep.ps1` (removes tracked files): the user runs it.
- The stash decision, and `--execute` on the real repo.
- Every `gh` call that changes GitHub state, the rename, the create, the push: the user's, or
  mine on an explicit yes at the time.

Tier: Standard. Branch: `chore/public-release-prep`. Fable verified the first draft on
2026-10-03; its blocker (person vs company name) became D2b, and all its other findings are in.
