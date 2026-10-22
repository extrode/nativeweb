# Todo — NativeWeb

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Now-ish

- **`skeleton/nuget.config` still maps `JauntyQ.*` and the two premium ids to `framework-github`.**
  The root config retired its `jauntyq-github` source on 2026-09-24 (it served nothing pinned,
  and the vulnerable package gate got 403 from it). The skeleton's source stays, since it serves
  `NativeWeb.*`, but its JauntyQ patterns are dead weight a stamped product inherits. Same
  change as the skeleton workflow gaps at the end of this file; stamped-product CI was out of scope here.

- **Point CI back at GitHub's runners when the Actions minutes reset (October 2026).** Since
  2026-09-24 the repository variables `CI_LINUX_RUNNER` / `CI_WINDOWS_RUNNER` send every job to
  the self-hosted runners in `scripts/ci-runner/` on the dev machine. Delete both variables
  (`gh variable delete CI_LINUX_RUNNER -R extrode/nativeweb`, same for Windows) and the jobs fall
  back to ubuntu-latest / windows-latest with no commit. Then stop the runners and remove them
  under Settings > Actions > Runners, or keep them as a spare.

- **Scaffold generator: a cascaded refusal hides the local fault.** A model whose reference
  property is `string` rather than `string?`, against a target that is itself dropped, is reported
  as `NWS003` alone; the author fixes the target and only then meets `NWS004`. A consequence of
  reporting against the surviving set, which is the right default -- the alternative reported
  against a lookup holding names the compilation was about to drop. Two rounds to a clean build
  rather than one, and nothing is wrong in between. Found by Fable reviewing
  `fix/scaffold-generator-remaining`, 2026-09-06.

- **Forgetting a seeded sample subject re-grants it on a hand re-apply of `0006_permissions.sql`.**
  Spec 026's `ForgetSubjectAsync` deletes the `nw_subject` row, and the migration's bootstrap CTE
  reads a missing row as first sight, so forgetting `operator` or `demo` and then re-applying the
  file by hand restores all four grants. `db/migrate.sh` will not do this on its own -- `is_tracked()`
  skips applied versions -- so it needs a person re-applying deliberately. Closing it wants a
  bootstrap marker the forget cannot remove, which is a new migration; sample-only either way.
  Found by Fable reviewing 026, 2026-09-04.
- **`AccessAdmin.ForgetSubjectAsync` answers 404 for an empty subject where its sibling POST
  handlers answer 400.** Unreachable through routing. Same review.

- **The three consumers were probed on 2026-09-01; seven gaps, written up in
  [`docs/architecture/004-consumer-readiness.md`](../docs/architecture/004-consumer-readiness.md).**
  Read that first — this entry is the backlog handle, not the finding. What it measured: `epass.io`
  declares two NativeWeb project references and uses **zero** NativeWeb types, having hand-ported
  the five files `libs/nativeweb.web` now ships; `justsmtp.com` cannot build at all, both because
  its probe path still names `extrode-legacy-org.com` and because seven of its files `using
  NativeWeb.SignIn.Epass`, a library that left on 2026-08-31; and `ghosthive.com/apps/web` is 61
  files of SvelteKit 5 on Deno whose recorded plan to adopt NativeWeb names the same dead path and
  two libraries that no longer exist.

  Work that lands here, in the order the document argues for:
  - ~~**G1 — pack and version the libraries.**~~ **Closed 2026-09-04 by spec 028:** one
    `VersionPrefix` in `Directory.Build.props` for all thirteen packable projects, an opt-in
    `IsReleaseBuild` so every unmarked pack is `-dev`, `tools/pack.ts` packing and then verifying
    each `.nupkg` (13 of 13 in 24.9 s), a tag-only `.github/workflows/publish.yml`, and
    `docs/02-reference/versioning.md` for obligation 2 of decision 004.

    **Two of decision 004's four obligations remain, and neither is work here.** Obligation 1 is
    the org-side package read grant; obligation 4 is repointing `justsmtp.com`, which
    `docs/architecture/004-consumer-readiness.md` §5 puts in that repository. **Nothing has been
    published yet** — the workflow exists and no `v*` tag has been pushed, so the pins in
    `skeleton/Directory.Packages.props` still resolve to nothing until a release is cut.
  - ~~**G2 — a breaking-change log in `docs/`.**~~ **Closed 2026-09-01 by spec 017:**
    `docs/02-reference/breaking-changes.md`, linked from the README, opening with the 017
    `ListAsync` change and carrying the 2026-08-31 removals of `nativeweb.signin.epass` and
    `nativeweb.auth` retrospectively.
  - ~~**G5 — list primitives.**~~ **Closed 2026-09-01 by spec 017:** `[ScaffoldSort]`, an
    allow-listed `?sort=`/`?dir=`, and keyset paging on an opaque cursor, replacing offset
    `skip`/`take` throughout. **Filtering was scoped out and stays out** — free-text
    `[ScaffoldSearch]` only, no structured per-field filters, no operators, no query language,
    because NativeWeb has no ORM and JauntyQ owns data access. What ghosthive hand-rolled as
    `ListQueryParser` and `FilterComposer` is not coming here; its keyset `Cursor` is.
  - ~~**G6 — READMEs for the six libraries without one**~~ **Closed 2026-09-02 by spec 018:**
    `nativeweb.web`, `.render`, `.forms`, `.security`, `.mail` and `.signin.testing` each have
    one, linked from `docs/README.md`. The three `.generator` projects stay without: a consumer
    references one as an analyzer and calls nothing it defines, so each is documented in the
    runtime library it serves.
  - ~~**G7 — an adoption guide for an existing app.**~~ **Closed 2026-09-02 by spec 018:**
    `docs/00-quick-start/adopting-in-an-existing-app.md`, checked by taking a stock
    `dotnet new web` project through every step of it.

  ~~**Needs a decision, not work — G3.**~~ **Answered, and built: spec 016 shipped
  `libs/nativeweb.signin` on 2026-09-01** — authorization-code with PKCE, the session cookie
  primitive and the introspection filter, with no provider flow, realm or client registry, on the
  `contrib.auth` side of the Django line the same way `django-allauth` is. What the three
  consumers each hand-rolled is now framework; migrating them is their own work, not this
  repository's.

  **Left alone deliberately — G4.** NativeWeb renders HTML and has nothing for ghosthive's
  JSON-only control plane. That may be the right boundary; the framework should say so rather than
  leave it implied.

- **Spec 011's backup and restore drill — R16 and R17, parked here on 2026-09-01 at the user's
  request.** Spec 011 is `Status: draft` for this and nothing else: T15's first third ran for
  real (`nativeweb-dev` serves `apps/sample` at `nw-dev.example.com`, two blue/green releases at
  1.774 s and 1.888 s), and the restore drill cannot start because no backup exists.

  **The two sentences that used to follow are superseded and kept for the trail.** They said spec
  011 was `Status: draft`, that `/etc/nativeweb/backup.env` was present and empty, and that the
  timer failed on `BACKUP_S3_BUCKET: set BACKUP_S3_BUCKET`. The keys were filled and the first
  backup taken on 2026-09-05, the drill ran the same day, and the spec is `Status: done`. The
  timer does fail nightly again as of 2026-09-06, but on a deleted endpoint rather than an empty
  variable.

  **The open question in front of this is answered, 2026-09-06: `pg_dump` earns its place.**
  A weekly, unrehearsed whole-instance export is no substitute for a daily dump, so the
  deliverable was a drill, not a deletion.

  Neither restore has been drilled from where the bytes will actually live. `restore.sh` was
  drilled on 2026-09-05 against MinIO, which is gone; the backup-tool restore has not been drilled at
  all. That is what keeps R17b open rather than merely pending.

  Whatever the answer, the credentials step is the operator's: a bucket, its keys, and an age
  keypair from `age-keygen -o /etc/nativeweb/backup-age.key` run **on the host**. A session must
  not generate that key, because the private half decrypts every backup ever taken. R15 stays
  conditional on `BACKUP_HEARTBEAT_URL`. `nativeweb-prod` is out of scope and untouched.

- **The security and mail tiers exist now — 227 facts, written 2026-08-31.**
  `tests/nativeweb.security.tests.unit` (174) and `tests/nativeweb.mail.tests.unit` (53) close the
  coverage gap recorded below; nine mutations across the two libraries were checked to confirm the
  tests can fail. Three defects were found and fixed in the same branch: `Passphrases.Verify`
  caught only `FormatException` where Geralt raises `ArgumentOutOfRangeException` below 45
  characters, so a truncated stored hash threw out of a sign-in path; `Masking.IpAddress` chose
  its separator by looking for a colon, so an IPv4 address with a port came back unmasked; and
  `EmailTemplates.ParseRecipient` accepts a line break immediately before the `@`, which MimeKit
  folds away — the test asserts the result carries no line break rather than a rejection that does
  not happen.

  **The three API decisions that came out of it were taken on 2026-09-01 and are done.**
  `StoreDataProtector.Unprotect` now throws on a missing `enc:v1:` prefix, with
  `allowLegacyPlaintext: true` as the opt-in for a product carrying rows from before its own
  encryption rollout; `ContactProtector`'s `realmId` is `scopeId` with the epass table names gone
  from its comments; and `FakeMaskDigest` and its `"fakemask"` subkey were removed as identity
  provider flow rather than primitive. Checked first: no sibling repo is affected — epass.io has
  its own `libs/epass.security` copies and the one app of its that references
  `nativeweb.security` uses none of these types.

  **Still open from that work:**
  - `Passphrases.NeedsRehash` has the same unguarded Geralt call `Verify` had. It is only ever
    called after a successful `Verify` today, so nothing reaches it with a malformed hash, but the
    asymmetry is a trap for the next caller.
  - Stale `obj/` build residue under `libs/nativeweb.security` and `libs/nativeweb.mail` still
    carries `epass.security` / `epass.mail` AssemblyInfo from before the 2026-07-26 rename.
    Untracked and ignored, so it affects nothing but a grep; `scripts/cleanup/stale-epass-obj.ps1`
    removes it.

- **Three-lens repo audit, 2026-08-31: 29 findings, none fixed yet.** Fable (`review-deep`,
  architecture-to-promise), glm-5.3 (`glm-think`, the untested security library) and qwen3.8-max
  (`qwen-review`, the three source generators), each given the repo's stated promise and asked to
  find where the code does not meet it. Raw output in `tmp/audit/{fable,glm2,qwen}.log`, scratch,
  so this is the durable record. **None has a shell**, so every finding below is structural until
  a test is written for it; the ones marked *confirmed* were checked here by reading the code.

  **The one that changes what the repo can claim: `libs/nativeweb.security` and
  `libs/nativeweb.mail` have zero tests.** *Confirmed* — nothing under `tests/` names
  `Passphrases`, `OpaqueTokens`, `StoreDataProtector`, `ContactProtector`, `Masking` or
  `Sha256Hex`. Eleven files of Argon2id, AES-GCM, blind-index HMAC and opaque tokens, exercised by
  none of the 657 facts, because their suites left with the identity provider. `nativeweb.security`
  is one of the two projects epass.io builds against. glm found 10 candidate defects in seven of
  those files on a single read, headed by: `Masking.cs:81` an IPv4 address with a port takes the
  IPv6 branch and is emitted unmasked; `StoreDataProtector.cs:75` a value whose `enc:v1:` prefix
  was stripped is returned verbatim as "legacy plaintext", so integrity is opt-out by deleting
  seven characters (*confirmed by reading*); `Passphrases.cs:63` a malformed stored PHC string
  throws out of `Verify` instead of returning false; `ContactProtector.cs:69` trim-only
  normalisation gives one phone number two different blind indexes.

  **`libs/nativeweb.security/ContactProtector.cs` is scope residue.** Its public API is
  realm-scoped (`EmailIndex(long realmId, ...)`) and its comments name `email_addresses` and
  `phone_numbers`, tables that exist nowhere here. It has zero in-tree consumers. Realms are on the
  must-not-return list; either the realm parameter goes or the file does.

  **Scaffold generator, 12 findings from qwen, three of them silent-wrong-answer:**
  inherited members are invisible because `ScaffoldGenerator.cs:124` uses `type.GetMembers()`,
  which returns only declared members, so every save resets a base-class property to `default`
  (*confirmed*); two `[Scaffold]` types with the same short name in different namespaces collide on
  the hint name `model.TypeName + ".Scaffold.g.cs"`, where `TypeName` is `type.Name`, and crash the
  consumer's build (*confirmed*); a second `[ScaffoldKey]` silently wins over the first at
  `ScaffoldGenerator.cs:186` with no diagnostic, against `ScaffoldAttributes.cs:115`'s promise of
  exactly one (*confirmed*). The rest are emission defects the forms generator already solved and
  the scaffold generator did not inherit: no keyword escaping (contrast `FormEmitter.cs:99`), no
  `SymbolDisplay.FormatLiteral` on `Route`/`Display`/`Label`, no rejection of generic or nested
  types (contrast NWF005), and `Build`'s `new()` against a positional record that has no
  parameterless constructor. Render generator: `TemplateModel.cs:143` reserves "the writer and
  implicit locale parameters" and the list contains neither name.

  **Architecture-to-promise, from Fable:** no principle violation reached a consumer — the only
  `System.Reflection` use in `libs/` is one documented assembly-version read. What it found instead
  is drift. `apps/sample/PublicArticleEndpoints.cs:40` attaches neither a rate-limit policy nor
  `AsPublicPage` while `skeleton/apps/__product__/Program.cs:77` teaches both, so a consumer
  copying the sample ships an unbounded public endpoint. Both trees carry a
  `wwwroot/vendor/nativeweb/boot.js` that nothing references after spec 015. The skeleton's
  `.csproj:46-52` names four OTel packages `libs/nativeweb.web/nativeweb.web.csproj:48-51` already
  supplies, with a comment pointing at telemetry code that moved.

- **justsmtp.com cannot build against this repository, twice over.** *Confirmed 2026-08-31.* Its
  `Directory.Build.props:51-52` probes for `extrode-legacy-org.com\NativeWeb`, a path that has not existed
  since the move to `extrode.com/nativeweb`, so `NativeWebRoot` resolves to nothing; and
  `apps/web/justsmtp.web.csproj` plus its integration tests reference
  `$(NativeWebRoot)\libs\nativeweb.signin.epass\nativeweb.signin.epass.csproj`, which left with the
  identity provider — 24 `using NativeWeb.SignIn.Epass` sit behind it. MSBuild only warns (MSB9008)
  on a missing `ProjectReference`, so this fails late and confusingly. Both fixes belong in
  justsmtp.com, not here, but nothing in this repo told it the project was going. epass.io by
  contrast is correct: it probes `..\nativeweb` and consumes only `nativeweb.render` and
  `nativeweb.security`. ghosthive.com consumes nothing from NativeWeb at all.

- **Audit sweep 2026-08-30, `chore/backlog-audit-2026-08-30`: 18 defects found and verified.**
  Two lanes on the `audit` class, each finding independently, then every claim handed to a
  `review-deep` (Fable) worker prompted to **refute** it. **18 claims, 18 confirmed, 0 refuted** —
  an unusually clean pass, and the reason to trust the list rather than a reason to relax about
  it. The evidence tables are `tmp/audit/{deploy,skeleton}.md` and the verdicts
  `tmp/audit/fable-{deploy,skeleton}.md`; both are scratch, so the durable record is here and in
  the fixes themselves.

  **Lane 1 — `scripts/deploy/**`, which has never run against a real host.** Eight defects, and
  the first four each stop the runbook dead on a clean Debian:
  1. `Caddyfile:34` uses `{$NATIVEWEB_HOST}`, but nothing makes `caddy.service` read the
     `/etc/default/caddy` that `provision.sh:286-293` writes — the site block gets an empty
     address and Caddy will not load the config.
  2. `provision.sh:93` creates `/var/log/caddy` as `root:root 0755` while packaged Caddy runs as
     `caddy`, so the global access log cannot open its file.
  3. `provision.sh:305` installs the firewall only if `/etc/nftables.conf` is absent or carries
     our marker — but line 49 of the same script installs Debian's `nftables`, which ships that
     file as a conffile. **The skip branch fires on every first provision, so R5 is unmet.**
  4. `deploy.sh:106` passes `READY_TIMEOUT=… RELEASES_KEEP=…` on the `sudo` command line, which
     Debian's default `env_reset` sudoers rejects outright.
  5. `restore.sh:101-103` derives the bucket prefix from the *current* hostname with no override,
     so the disaster-recovery path — rebuild the host, then restore — cannot find the backups.
  6. `restore.sh:161,170`: `provision.sh:256` scopes the `pg_hba` peer map to database `nativeweb`
     only, so the documented `nativeweb_drill` restore falls through to `local all all peer` and
     cannot authenticate. Only the production overwrite works, which is the wrong way round.
  7. `deploy.sh:321` runs `./db/migrate.sh`, which is mode 100644 in the index; nothing in
     `build.sh` or `deploy.sh` adds an exec bit, so a release cannot be promoted from any build
     host that is not a drvfs checkout.
  8. `provision.sh:277` enables both colours before either exists, so a host rebooted before its
     first deploy burns the units' start limit and sits failed.

  **Lane 2 — `skeleton/` against `apps/web`.** Ten drifts, every one a fix the real app made and
  the template never took, so each is inherited by every product generated from it. Worst first:
  bare `AddAntiforgery()` (no `__Host-` prefix, `SameAsRequest`, plantable from a subdomain);
  `UseAntiforgery()` with no rejection middleware, so the first form POST is a 500 rather than a
  400; `AddUserSecrets` unguarded *and* outranking environment variables in production;
  `/readyz` with a per-request `SELECT 1`, no cache and no policy; OTel with no
  `QueryRedactingProcessor` and no resource attributes; startup errors thrown rather than
  `FailStartup`'s `Environment.Exit(78)`, which under NativeAOT on Windows hangs a service instead
  of exiting; `security.txt` routes with no rate-limit policy; JauntyQ pinned at 0.2.0 against the
  root's 0.4.0; a single unguarded `JauntyQRoot` probe that breaks every worktree build; and a
  comment naming epass.io. Fable added one correction of its own: the finding claimed the comment
  was the only surviving real-name leak, and `wwwroot/css/{tokens,components}.css` name `epass.zip`
  too.

  **Also found, and separate:** the repo's own `assertionless-test` check has no notion of a
  project-local assertion helper, so `AssertRfcError(...)` and `AssertSingleDiagnostic(...)` read
  as no assertion at all. Running every mechanical check over all 130 slices produced 124
  candidates, of which **52 are that one false positive** — the daily audit spends its refutation
  budget on them. One line in `checks/assertionless-test.ts` fixes it.

  **All 19 are fixed on the same branch, 2026-08-30.** Deploy lane: eight fixes plus ten new
  `DeployScriptTests` facts. Skeleton lane: ten drifts closed — five mechanical, five behavioural
  (four new files: `Middleware/AntiforgeryValidationMiddleware.cs`, `StartupConfigGuards.cs`,
  `ReadinessCache.cs`, `QueryRedactingProcessor.cs`) — plus `SkeletonParityTests`, **14 facts, and
  the first parity coverage this repo has had**: nothing in `NativeWeb.slnx` builds `skeleton/`, so
  every one of these ten was a thing a stamped product would do WRONG rather than fail to compile.
  Six mutations, six distinct failures. `assertionless-test` fixed and the sweep re-run: **124 →
  83 candidates**. Suites after: 676 web-unit (+14), 157 render, 64 scaffold, 184 signin, 16
  sample; `tools/audit` 255; structure-audit 0 errors.

- **`nativeweb.signin.epass.tests.unit` failed 1 of 184 once on 2026-08-30 and has passed 5 runs
  since**, in `.worktrees/audit-sweep`. The failing test's name was lost to a shell loop that
  filtered it out, so there is nothing here to act on beyond knowing that tier has produced one
  unexplained red. Worth capturing the name if it recurs.

- **CI is red on every push, and the cause is a missing package grant — not a missing package.**
  Measured on run `33259408524` (2026-08-29): `NU1301` … `403 (Forbidden)` against
  `https://nuget.pkg.github.com/extrode/download/jauntyq.runtime/index.json`, killing Restore in
  all three jobs. `gh api orgs/extrode/packages?package_type=nuget` returns `JauntyQ.Runtime`,
  `JauntyQ.Generator` and `JauntyQ.Schema.Contract`, each `private`, owned by `extrode/jauntyq`,
  each at **0.4.0**, matching the pins in `Directory.Packages.props:22-23`. So the packages were
  published after all — this is the *next* failure, not the `NU1101` empty-feed one from 2026-08-27
  recurring. `.github/workflows/ci.yml:12-16` names the requirement: the job's own `GITHUB_TOKEN`
  works "only because the extrode org grants this repo read access to the jauntyq packages", and
  that grant is missing. **The fix is UI-only and the user's** — no REST endpoint exists: for each
  of the three, `github.com/orgs/extrode/packages/nuget/package/<name>` → Package settings → Manage
  Actions access → Add repository → `extrode/nativeweb` → Read. Include `JauntyQ.Schema.Contract`,
  which no error names but which resolves transitively once the other two do. Keep them private
  (`AGENTS.md` forbids redistributing JauntyQ binaries); the grant is per-repository and publishes
  nothing. Widening to a PAT was already rejected in the workflow comment at lines 23-26.
- ~~**Five packable `libs/nativeweb.*` csproj files still say `<Authors>extrode-legacy-org</Authors>`**~~ —
  **done 2026-08-29**, `fix/stale-extrode-legacy-org-references`. **Ten, not five.** `<Authors>` is now central
  in `Directory.Build.targets` beside the `Copyright` that already said Extrode LLC, and the ten
  per-project lines are gone; verified end to end, `dotnet pack libs/nativeweb.render` produces
  `<authors>Extrode LLC</authors>`. New `PackageMetadataTests` pins both halves: one central value,
  and no per-project `<Authors>` — that file is imported *after* each csproj, so a local one is
  silently overridden and reads as though it works. Same branch fixed
  `skeleton/nuget.config`'s `framework-github` feed, still pointing at
  `https://nuget.pkg.github.com/extrode-legacy-org`, and the gap that hid it:
  `OrgReferenceTests.NoPackageFeedPointsAtTheOldOrg` scanned the root config and the workflows but
  not the skeleton config, so it was green on the one file that had rotted.
- **The ISL-CLA draft banner in `CONTRIBUTING.md` comes out when the agreement is marked final.**
  Watch <https://islamiclicense.org/isl-cla/1.1/CLA.md> — **published and live since 2026-08-30**
  (200, 29,482 chars), and still `**Status:** Draft — not in force` at line 3 with the
  **DRAFT — NOT YET IN FORCE** banner at line 9. So the banner in `CONTRIBUTING.md` stays. Nothing
  else in the file changes; `LicenseFilesTests` already pins the parts that must survive.
- ~~**Re-check both adoptions against the published texts once islamiclicense.org deploys.**~~ —
  **done 2026-08-30**, `docs/isl-1-1-published-verification`. All four URLs return 200; the
  published `/isl-netc/1.1/LICENSE.md` and `/isl-cla/1.1/CLA.md` are **byte-identical** to
  `site/isl-netc/1.1/LICENSE.md` and `site/isl-cla/1.1/CLA.md` in the checkout the bumps were taken
  from, and the published 1.1 differs from this repo's `LICENSE.md` at one line, the completed
  §1.2. The five adoption items survived verbatim (CLA 1.1 lines 98–113). The **two §9.2
  placeholders did not, and never existed in 1.1**: 1.0 §9.2 carries a literal
  `` `[JURISDICTION — to be completed by the Licensor.]` `` (line 342), while 1.1 §9.2 (line 417)
  states the law and the forum are "stated by the Licensor where it adopts this Agreement" and the
  adoption section says "This Agreement carries no blanks to fill". `CONTRIBUTING.md` rows (c) and
  (e) described them as placeholders being completed — 1.0's shape, carried forward by mistake in
  the 1.1 bump — and were reworded on the same branch. The stated values are unchanged.
- ~~**Re-sync to ISL-NETC 1.1 once islamiclicense.org publishes it.**~~ — **done 2026-08-30**,
  `docs/isl-netc-1.1`. Every item below landed. Two corrections to what this entry claimed:
  the pinned `https://islamiclicense.org/isl-netc/1.0/LICENSE.md` **does not 404** — spec 003 gave
  every version a permanent URL, and the last line of this entry was already stale when written.
  And the source used was the local `ikhbat.org/islamiclicense.org` checkout (`ad5291a`, tree
  clean), not the site. The re-synced `LICENSE.md` differs from that checkout's
  `site/isl-netc/1.1/LICENSE.md` at one line — the completed §1.2 — confirmed by `diff`.
  **Verified against the live site 2026-08-30** (entry above): the published text is byte-identical
  to that checkout, so the local-source caveat is discharged. Note for the next agent that hits
  this: `curl` on this machine fails every https request with code 43 / `000`, which reads exactly
  like blocked egress and is not — `deno eval 'await fetch(...)'` gets 200 from the same shell.
  <details><summary>original entry</summary>

  Their `dev` is 25 commits ahead
  of `main` (pushed to `origin/dev`, but the site deploys from `main` and there is no workflow, so
  it is a manual step of theirs); the live site serves 1.0 and their spec 002 froze the 1.0 texts at
  `/isl-x/1.0/LICENSE.md`, so nothing here is broken and nothing is urgent. When it lands, one
  branch does all of it: replace `LICENSE.md` with the 1.1 text and fill `[LICENSOR]` with
  **Extrode LLC**; bump SPDX to `LicenseRef-ISL-NETC-1.1` in `NOTICE.md`, `README.md`,
  `Directory.Build.targets` and `LicenseFilesTests`; **rewrite
  `LicenseTextIsNotEditedToNameThisLicensor`, which asserts `LICENSE.md` contains no "Extrode" and
  inverts on a filled 1.1 copy**; strip the now-unnecessary Licensor-override argument from
  `NOTICE.md`; and update `CONTRIBUTING.md` row (a) and row (d), which both cite the 1.0 text.
  Until then `NOTICE.md:13` and `README.md` point at `https://islamiclicense.org/isl-netc/`, which
  becomes latest-1.1 on their deploy — either bump, or repoint those two at
  `https://islamiclicense.org/isl-netc/1.0/LICENSE.md`, which 404s today.
  </details>
- ~~**Send islamiclicense.org one finding:** the `[LICENSOR]` carve-out lives only in the
  instructions~~ — **closed 2026-08-29** by their spec 003 (`7bfe873`). Every 1.1 text's *Notice on
  Reproducing This License* now says completing the bracketed fields "is not a modification of this
  text for the purposes of this notice", and that a copy so completed may be published under its
  ISL name. The companion finding — ISL-CLA amended in place under a fixed version — closed the
  same day: 1.0 is frozen back to four items and the revisions are `ISL-CLA-1.1.md`.
- ~~**22 cleanup scripts hardcode `C:\home\code\extrode-legacy-org.com\NativeWeb`**~~ — **done 2026-08-29**,
  `chore/backlog-sweep`. 20 rewritten to `C:\home\code\extrode.com\nativeweb`. Two left alone:
  `move-to-extrode.ps1` and `relocate-nativeweb-to-extrode-legacy-org.ps1` are records of completed
  relocations, and rewriting their paths would make each contradict its own first paragraph.
  `extrode-legacy-org.com\manzil.my` untouched — still there. `CleanupScriptPathTests` matches the assignment
  rather than the text, so a script may narrate a move it performed, and asserts the absence of the
  abandoned path rather than the presence of the current one, so it holds on a checkout elsewhere
  and on CI.
- ~~**`apps/web/SecurityTxtEndpoint.cs` hardcodes `security@epass.io`.**~~ — **done 2026-08-29**,
  `chore/backlog-sweep`. Now `NativeWeb:SecurityContact`, validated at startup the way
  `CanonicalBaseUrl` is (exit 78 on a supplied-but-unusable value, blank valid). Blank serves **no**
  `security.txt` rather than one carrying a placeholder: a wrong `Sitemap:` line is discarded by the
  crawler, a wrong `Contact:` line is acted on and the reporter cannot tell it went nowhere. A
  newline in the value is refused, and mutation testing is how I know that guard is reachable rather
  than defensive padding — `Uri.TryCreate` accepts
  `https://e.com/r\nContact: mailto:evil@x.net` as a valid absolute https URI, and `tel:` likewise.
  The skeleton took the same change; a stamped product had been shipping
  `security@<product>.example`, a domain it does not own.
- ~~**The sign-in library's AOT publish gate is in the solution but runs nowhere automatic.**~~ —
  **done 2026-08-29**, `chore/aot-gate-in-ci`. Added to the `aot-publish` job as a second publish,
  which it has to be: `apps/web` holds no ProjectReference to `nativeweb.signin.epass`, so the
  library's IL never reached the existing native link and a green `aot-publish` said nothing about
  it. Two mechanisms that both looked like coverage, neither of which was — `dotnet test` collected
  zero facts from it and exited 0 without printing a line.
  `AotGateCoverageTests` derives the gate list from the projects (`PublishAot=true` **and**
  `IsTestProject=false`, both) rather than restating it, so adding another gate project fails the
  suite until `ci.yml` catches up; a hand-maintained list would be the same failure one level up.
  Re-run locally 2026-08-29 before wiring: 0 warnings, no IL2026, no IL3050.
- ~~**`SdkPinTests.NoWorkflowInstallsAnSdk` scans gitignored scratch.**~~ — **done 2026-08-29**,
  `chore/backlog-sweep`. Now `git ls-files --cached --others --exclude-standard`, the call
  `NewProductStampTests` already makes: uncommitted workflows are still governed, ignored ones are
  not, and `.gitignore` stays the one place the distinction lives. Two facts added — a decoy
  checkout written under `tmp/` that the scan must not see, which fails against the old tree-walk,
  and a floor of two workflows, because a scan matching nothing reported no offenders and read as a
  clean bill.
- ~~**The stamped product's README omits the two things its test tiers need.**~~ — **done
  2026-08-29**, `chore/backlog-sweep`. Both are in `skeleton/README.md`'s Build-and-test section now.
  Verifying it by stamping a product turned up something larger, fixed in the same branch:
  **`skeleton/Directory.Build.props` was not valid XML** — `--` inside an XML comment — so MSBuild
  refused to load it (`MSB4024`) and **no stamped product had ever built**, failing on its own
  README's first command before compiling a line. It survived because nothing reads the skeleton as
  XML: the suite covers `apps/` and `libs/`, whose build files MSBuild loads every run, while the
  skeleton's are copied as text and parsed by nobody until a product exists. `BuildFileXmlTests` now
  parses every tracked `.props`/`.targets`/`.csproj`/`.slnx`/`nuget.config`/`.nuspec` via
  `git ls-files`. Verified by stamping `Sectest` and building it: 0 warnings, 0 errors.

- ~~**`apps/web`'s only launch profile is HTTP, and the app is HTTPS-only in practice.**~~ —
  **done 2026-08-29**, `chore/backlog-sweep`. The profile is `https://localhost:5444`, and both
  `README.md` and `getting-started.md` drop the `--urls` flag they used to tell the reader to pass.
  One profile, not two: an HTTP one beside it is the same trap with a name to select it by.
  `LaunchProfileTests` asserts that, and asserts both documents name the URL the profile listens on
  — a profile and a document that disagree send a newcomer to a port nothing answers, and neither
  file can tell.

Small, clear, and could be picked up without a design decision.

- **`NativeWeb.slnx` cannot be built from a `.worktrees/<branch>` checkout.** Lines 31–32 carry
  `../jauntyq/src/...` as literal solution-relative paths, which resolve to `extrode-legacy-org.com/jauntyq`
  from the main checkout and to `NativeWeb/.worktrees/jauntyq` from a worktree, where nothing
  exists. Restore fails as MSB3202 before a line of the branch's own code is compiled.
  **Per-project builds work** (`dotnet build tests/<project>`) and that is how spec 009 was built
  and tested; only `dotnet build NativeWeb.slnx` is affected.

  Not fixed with 009, because no single relative path is correct from both locations — the fix is
  a decision about how the solution references JauntyQ at all. Two candidates: reference JauntyQ
  as a package the way justsmtp already does (it pins `v0.3.0` and dropped its checkout on
  2026-08-18), or drop the two JauntyQ projects from the solution and let the ProjectReferences in
  `libs/nativeweb.data` pull them in. The first is the direction the org is already going.

  The `Directory.Build.props` half of this **was** fixed in 009: `JauntyQRoot` now probes two
  depths, so a worktree resolves it with no `-p:` flag.

- ~~**`docs/specs/008-epass-signin-library/008-spec.md` still says `Status: draft`**~~ — **done
  2026-09-02**, `docs/008-close`: the user chose to amend R1, R11 and R12 rather than change the
  code, and R1's evidence had gone stale anyway — the `IConfiguration` overload the review said
  was missing arrived with spec 016 and the skeleton wires sign-in in three lines. The spec's
  "Closed" section has the R-by-R resolution. Original entry: and after the
  review, **it should**. Reviewed 2026-08-25, `docs/008-spec-status`; the R-by-R table and the
  evidence are in the spec's new "Acceptance review" section. Twelve of fifteen are met and
  evidenced. Three cannot be flipped by anyone but the user, because in each the shipped code took
  a deliberate decision the criterion does not describe:
  - **R1** "under 10 lines" vs justsmtp's real 32-line wiring block — 5 lines of library contract
    plus 27 of hand-rolled `IConfiguration` reading, because `AddEpassSignIn` has no
    `IConfigurationSection` parameter.
  - **R11** "None acquires a library-supplied default" vs `CookieName`, which has one, and a good
    one.
  - **R12** "no behavioural change" vs two: the `v1`→`v2` ticket format signs every justsmtp user
    out once at cutover, and the "check your email" page no longer shows the address.

  R12's other half **is** closed: `eb06a3a` in justsmtp.com deleted eight of the nine `Auth/`
  files, rewrote the ninth as its `ILoginFlowStore`, and is merged into that repo's `dev`.
  Four T18 design calls from 2026-08-19 are also still unanswered in `~/.claude/away-questions.md`
  — a public `BeginLoginAsync`, `OnSignedIn` → `WithResolver`, splitting `LoginPagePath` out of
  `LoginPath`, and rejecting a non-loopback `http://` BaseUrl. None blocks the status flip; all
  four are cheaper now than after a second consumer.

- ~~`skeleton/.github/workflows/ci.yml` pins `ilammy/msvc-dev-cmd@v1`, a mutable tag.~~ **Done
  2026-08-17**, `chore/skeleton-action-pin`: pinned to
  `0b201ec74fa43914dc39ae48a89fd1d8cb592756`, resolved with
  `gh api repos/ilammy/msvc-dev-cmd/git/ref/tags/v1` rather than copied on faith, and it matches
  the SHA this repo already pinned. `ActionPinTests` now guards **both** workflows: every
  non-`actions/*` `uses:` must be a 40-hex SHA. `actions/*` stays on tags deliberately -- GitHub
  publishes it from the trust boundary already running the workflow, so a SHA buys nothing there.
  The test has two guards against passing vacuously: one asserting the scan finds both
  repositories' workflows, one asserting at least one third-party action is actually under test.

- ~~**Two `migrate.sh` runs at once will double-apply a migration, silently.**~~ — **done
  2026-08-18**, `fix/migrate-run-lock`. A coprocess psql holds a session-level
  `pg_advisory_lock` for the whole run, taken before the first `is_tracked` read and released
  by an EXIT trap; a second runner prints `wait  another migrate.sh is running against <db>`
  and queues. Verified for real by `db/test-run-lock.sh` (7 checks, ~1 min against an
  already-migrated database), with three structural facts in `MigrateRunnerTests` so deleting
  the lock also fails the normal suite. Two things measured on the way, both recorded in the
  script: psql's stdout is block-buffered through a pipe so the readiness marker has to come
  back on stderr via `RAISE WARNING`, and psql on Windows writes CRLF so the marker read has to
  strip the CR.

- ~~**Migrations now hold locks for a whole file — check the long ones before the cutover.**~~ —
  **reviewed 2026-08-18**, `docs/02-reference/migration-lock-review.md`. Nothing to change today;
  the review
  is the deliverable. `0021_realm_scoping` is the one that would hurt: for six tables it does
  ADD COLUMN → whole-table UPDATE → SET NOT NULL → ADD FK → CREATE INDEX, so wrapped it holds
  ACCESS EXCLUSIVE on every table a sign-in touches at once. `0013_cascades_and_fk_indexes` is
  second. Every CONCURRENTLY migration already carries `@no-transaction` and is unaffected. The
  doc lists what to do if migrations are ever pointed at a live database rather than a
  provisioning one.

- **The JauntyQ source generator drops its whole output on incremental rebuild, intermittently.**
  Hit twice on 2026-08-17, both times in `libs/nativeweb.auth`: the entire `JauntyQ.Generated`
  namespace vanished, so every file in the project failed CS0246 — including files the edit had
  not touched. A clean rebuild fixed it both times, and nothing was wrong with the source.
  Dangerous because of what it looks like: the second occurrence buried a genuine compile error
  of mine under 40 spurious ones, and the instinct is to "fix" code that was never broken. If
  the whole namespace disappears at once, rebuild clean before believing the errors.
  Not diagnosed. Reproducing it means catching the flake with binlogs on, which nothing here
  does routinely; the generator lives in the JauntyQ checkout, so the investigation is over
  there rather than in this repo. Recorded 2026-08-17.

- **JauntyQ package skew: watch for it recurring.** Closed for now — JauntyQ 0.2.0 published
  2026-08-17 and the pins here moved to match, verified by building the solution in package mode
  against the 0.2.0 nupkgs (0 warnings, 0 errors) and reproducing the old `JNT8004 No index
  covers ...` failures against 0.1.0 to prove the probe reaches the generator.

  Worth keeping in mind because **nothing in the build can detect this class of skew.** The
  packaged generator is a compile-time source generator, so its diagnostics fire in *this* repo,
  not in JauntyQ's suite — a green JauntyQ build says nothing about whether consumers compile.
  0.1.0 predated migration-parser index-DDL support (JauntyQ's own `CHANGELOG.md` recorded it as
  a known limitation), and since NativeWeb declares its whole schema in migration DDL, the
  packaged generator saw every index as absent. The local checkout was 1079 commits past the tag
  and still stamped `<Version>0.1.0</Version>`, identical to the published package, so no version
  string distinguished them.

  If package-mode CI ever fails on a diagnostic that a local checkout build does not reproduce,
  suspect skew before suspecting the code, and check how far the checkout has drifted past the
  pinned tag (`git describe`). Do **not** suppress `JNT8004` to get a green badge: it guards
  security-sensitive lookup paths, and suppressing it hides the skew instead of closing it.

- ~~IdP: `Redirects.To` answers a no-JS POST with 302 where 303 is correct~~ — **done
  2026-08-18**, `fix/redirect-see-other`, on the user's call. A POST now gets 303, a GET keeps
  302, and the pair is asserted deliberately in `RedirectStatusTests` so a later blanket edit to
  either shows up. Only two existing assertions had to change, both of which were asserting the
  status of a no-JS POST. Nothing observable changes in a browser; the reason to say it is that
  the OAuth surface is followed by native and machine clients whose stacks are less uniform
  about the 302 convention.

- ~~`AppProcessFixture.DeleteCert` catches only `IOException` … and `_certPath` is never
  nulled~~ — **fixed 2026-08-16** in `fix/v9-fable-findings`; it catches every exception and
  clears `_certPath` either way.

- ~~`RunAsync` in `AppProcessFixture` puts no overall timeout on the inner `dotnet publish`~~ —
  **done 2026-08-17**, `fix/idp-correctness-backlog`. 15-minute ceiling, killed with
  `entireProcessTree: true` so the compiler and linker it spawned do not keep holding the output
  directory. Output now drains through the `OutputDataReceived` events into a `BoundedLogTail`
  rather than two `ReadToEndAsync` calls, so a timeout can report the last 200 lines instead of
  nothing. `ProcessRunTests` covers all three exits.
- ~~No configuration reference exists~~ — **already done; entry was stale, checked 2026-08-17.**
  `docs/02-reference/configuration.md` (201 lines) documents all 32 `NativeWeb:` keys plus
  `ConnectionStrings:NativeWeb`, and gives `ForwardedHeaders:KnownProxies` its own section
  precisely because its default collapses rate limiting into one shared partition. It is
  self-guarding: `tests/nativeweb.web.tests.unit/ConfigurationReferenceTests.cs` asserts in *both*
  directions — every key the source reads is listed, and every listed key is one something reads
  — with a third test that fails if either scan finds nothing, so the pair cannot pass by
  comparing two empty sets. `NativeWeb:Smtp:*` comes from `SmtpOptions` by reflection rather than
  from source literals, since the section is bound and its sub-keys appear as literals nowhere.
  The only apparent gap, `NativeWeb:EpassLegacyMode`, is not a real key: it is a negative fixture
  in `StartupConfigGuardTests` proving the legacy-key guard matches `Epass` as a whole segment
  only, and documenting it would be wrong.

## Later

Real, but needs a decision, a dependency, or more information first. Say which.

- **Strong-name the sign-in assemblies, or accept that `InternalsVisibleTo` is a simple-name
  grant.** Spec 009 R3 says the bound on ticket minting "is the compiler's". Against an accident
  it is; against someone who deliberately names an assembly `nativeweb.signin.epass.testing` it is
  not, because neither assembly is strong-named. **Needs a decision** — strong-naming is repo-wide,
  affects every package this repo ships, and is not something 009 should have taken on its own.
  Raised by 009's review, 2026-08-25.
- **`OnSignedIn` has the same last-wins defect `OnAddressSubmitted` just had.** A second call
  appends a second `SignInResolver` and `GetService` returns it, so the first is silently
  unreachable — no throw at registration, at container build, or in `Validate()`. Fixed for the
  policy under 009 because that seam is 009's; **left for the resolver because it is 008 behaviour
  a consumer may already depend on**, and changing it is a decision rather than a fix. One line in
  `EpassSignInBuilder.OnSignedIn` when taken. Raised by 009's review, 2026-08-25.
- **`ClearRejected()` sets no `no-store`.** `SessionAuth.Clear` now does, for both sign-out paths,
  but the authenticate path writes its own cookie deletion when it rejects a cookie and does not.
  Lower stakes — that response is whatever the consumer's page was — but it is the same drift.
  Noted rather than fixed: out of 009's scope. Raised by 009's review, 2026-08-25.

- ~~**The three `uses:` actions in `ci.yml` still target the Node 20 runtime, which GitHub has
  deprecated.**~~ **Done 2026-08-20**, branches `chore/bump-actions-node24`,
  `chore/drop-msvc-dev-cmd`, `chore/drop-setup-dotnet`. Two of the three were deleted rather than
  bumped, which is why the entry's "blocked on upstream releases" premise never had to hold:
  `msvc-dev-cmd` set up a toolchain the ILCompiler locates itself, and `setup-dotnet` installed an
  SDK both hosted images already ship. `actions/checkout@v7` is the only `uses:` step left in
  either workflow. The SDK requirement now lives in `global.json`. Full reasoning and the
  measurements in `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.

- ~~**Antiforgery is validated before `HasFormContentType` at all 23 POST sites — decide, don't
  drift.**~~ **Decided and done 2026-08-17** (user: "swap to 415 first"), branch
  `fix/415-before-antiforgery`. The count was 26 validating handlers, not 23: 17 carried the
  400/415 pair and were swapped, six read the form through a tolerant ternary with no 415, and
  three accept a request that may or may not be a form (WebAuthn enroll/verify, sign-out) and
  had to keep accepting both. The "lets a caller learn whether its content type would have been
  accepted" argument was checked and does not hold: the 415 decision reads only the caller's own
  `Content-Type`, never server or session state, so it is the same answer for everyone. Guarded
  by a source-order sweep (`ContentTypeGuardOrderingTests`) plus HTTP-level facts
  (`ContentTypeGuardTests`).

- ~~**The six tolerant-ternary handlers still answer a misleading 400 for a JSON body.**~~
  **Decided and done 2026-08-18** (user: "extend the 415 sweep to the six handlers"), branch
  `fix/415-tolerant-ternary-handlers`. `AdminEndpoints.PostApplicationDisable` /
  `PostApplicationRotateSecret` and `SettingsEndpoints.PostTwoFactorDisable` /
  `PostRecoveryCodesRegenerate` / `PostWebAuthnRemove` / `PostSessionsRevokeOthers` each got the
  same guard ahead of `ValidateRequestAsync`, and the ternary they read the form through became
  a plain `ReadFormAsync` — there is no longer a path where the form is absent. 17 guarded
  handlers became 23, three remain deliberately unguarded (WebAuthn enroll/verify, sign-out).
  Behaviour proved by `AuthenticatedContentTypeGuardTests`, which needs a session for all six
  and an operator for two; the source-order sweep's 415 floor moved 15 → 20 so the six cannot
  regress silently.

- **A session key that cannot be a raw `Guid`.** Spec 007 routes every session read, write and
  delete through `SessionToken`, but `Guid.Parse(token.CookieValue)` still recovers the plaintext
  in one hop, so a new write path could bypass the hash and would compile. Making that
  impossible means the data layer taking its own key type rather than `Guid` — a JauntyQ-wide
  change, hence a decision, not a fix. Recorded 2026-08-16, from the spec 007 review; the spec's
  R4 says the same rather than claiming to be met.

- ~~**`migrate.sh` applies and records in separate connections.**~~ **Done 2026-08-17,
  `fix/migrate-record-in-transaction`.** `apply_and_record` now pipes the migration and its
  `schema_migrations` row into one `psql --single-transaction`, so either both land or neither
  does. Files that cannot be wrapped are detected by content rather than listed — `CONCURRENTLY`
  (six migrations; Postgres rejects it inside a transaction block) and an explicit `BEGIN` (0054
  only) — so a new migration of either kind needs no edit to the runner.

  **The consequence was worse than this entry claimed.** Measured against scratch databases with
  a migration that creates a table then calls a missing function: the old runner committed the
  CREATE TABLE, recorded nothing, and then failed on `relation already exists` on *every*
  subsequent run. Not "re-applies it" — a database no re-run can fix without someone editing
  `schema_migrations` by hand. The new runner rolls the whole thing back and fails the next time
  on the real error, so fixing the SQL is the whole recovery. Original note follows.

  `app_psql -f "$f"` then
  `record "$version"` (`db/migrate.sh:53,79-80`), so a crash between the two commits the
  migration and loses the tracking, and the next run re-applies it. Harmless for DDL, which is
  what every migration before 0054 is; not harmless for a data `UPDATE`. 0054 works around it by
  writing its own `schema_migrations` row inside its transaction. The general fix is for the
  runner to record inside the same transaction it applies in, which is a change to how it invokes
  psql. Noted 2026-08-16.

- JauntyQ multi-result-set batching is **deferred upstream** (JauntyQ `80af756`, 2026-08-03; the
  ask is recorded in that repo's `work/todo.md` under "Later"). That was the whole point of
  `docs/jauntyq/2026-08-02-001-result-set-features.md`: round trips, not query cost. What did land
  is the window-function polish — `COUNT(...) OVER (...)` now infers `bigint NOT NULL`, so
  `-- @type` is no longer needed for the paginate-with-total shape, and JNT1008 makes a
  two-statement file an error instead of a silent mis-parse. All three consumers build 0/0 against
  it (verified 2026-08-03); no existing query was mis-parsing and none uses `OVER(` yet.
  **Do not re-propose the perf plan's item 4 (fold count into the page query) on the old
  rationale.** Only its ergonomic objection was removed. The design objection stands — counts
  repeating their list predicate verbatim is deliberate and was reviewed 2026-08-02 — and spec
  006's output cache already took `/u/{username}` to 0.51ms/1.51ms from 7.41ms/20.56ms, so the
  anonymous traffic it would speed up no longer reaches PostgreSQL at all. The case that *is*
  still open is the authenticated list pages (a user's own bookmarks, their tag lists), which are
  never cached and still run count-then-page. Different case, needs its own measurement.
  **Not measurable here any more, checked 2026-08-17: those pages are not in this repo.** Spec 003
  extracted the bookmarking app to manzil.my, and `apps/` now holds `web` alone — no bookmark or
  tag endpoint, route or query file survives, and the IdP has no paged list page at all. So the
  item is misfiled rather than open: it belongs to manzil.my's backlog, against manzil.my's schema
  and traffic, and any measurement taken here would be of code that no longer exists. Carry it
  across before acting on it; do not re-open it as NativeWeb work.
- CSRF (user, 2026-07-30): the framework antiforgery is kept for its BREACH-masked token, but the
  user dislikes how the `<meta name="request-verification-token">` + hidden-input plumbing looks.
  Candidate: a Rails-style per-session token behind a small helper so the markup reads cleaner —
  needs a decision on trading away per-request masking.
- Render engine deferred review findings (V2 review, 2026-07-31; the fixable ones were fixed
  in-branch, these need design work):
  - Layout-as-partial params: `EmitPartialCall` binds `target.Params` rather than the composed
    parameter list, so a `@layout` page called as a partial gets the wrong signature
    (pre-existing, found in the spec 004 B1 round-2 review, 2026-08-01; no current template
    hits it).
  - URL-context safety: `href="{{Url}}"` with a `javascript:` value passes both encoder paths
    untouched (same gap as Razor). Full fix is HTML-context-aware generation; interim is the
    documented convention that URL values from user data get scheme-validated in C#.
  - ~~Unquoted-attribute interpolation (`<a href={{Url}}>`) is injectable via space/`=`; the
    generator could detect-and-reject interpolation into unquoted attributes.~~ Done
    2026-08-17 (`feat/render-diagnostics-locations`) as NWT016, which covers the
    attribute-name position (`<div {{X}}>`) too — the same escape by a shorter route.
  - ~~NWT diagnostics all report at `Location.None`; parser would need to carry line/col offsets
    to point at the token.~~ Done 2026-08-17. Parse-stage findings point at the token;
    post-parse ones (layout not found, class collision) point at the file, which is still a
    path an editor can open.
  - ~~`@param` type text goes into the generated signature unvalidated (typo = raw C# error, not
    NWT003).~~ Done 2026-08-17 — a plausibility check, not a type parser: it catches a type
    with a space in it (the one mistake `@param`'s last-space split creates) and leaves a
    well-formed name that does not exist to the C# compiler, which words it better.
  - No reference page lists the NWT diagnostics for template authors; each descriptor's message
    is the only documentation, and there is now no one place to see the sixteen of them.
  - No escape hatch for a literal `{{` in template text (would break Vue-style inline syntax if
    ever needed; Alpine/htmx don't use it).
  - Diagnostics tests discard the output compilation, so a template emitting invalid C# only
    fails via the test project's own build, not a targeted assert; no incremental-caching
    step-reuse test either.
- i18n deferred review findings (V3 review, 2026-07-31; the mechanical ones were fixed
  in-branch, these need design work or a future trigger):
  - Locale negotiation runs before `UseAuthentication`, so the (deliberately unwired)
    `UserPreference` hook would always see an unauthenticated context — move the middleware
    after auth when the hook lands with IdP page localization (comment in Program.cs marks it).
  - One NWT014/NWT015 suppresses the whole generated `Locales` class, so every `Locales.*`
    reference cascades CS errors that bury the real diagnostic; a best-effort/empty-catalog
    emission would keep it legible. Same shape for missing en.json → every `{{t:}}` fires NWT013.
  - Locale ids are two-subtag only: `zh-hans-cn` would get wrong `Lang` casing and inherit from
    `zh`, never `zh-hans` (documented at `LocaleCatalog.ToHtmlLang`); extend if a script-tagged
    locale ever ships.
  - Sanitize boundary is inconsistent in mail: renderer args are control-char-stripped but
    catalog-sourced subject/text are not (MimeKit encodes headers, so not injectable), and
    `Sanitize(linkUrl)` mangles a control-char URL into a different URL rather than rejecting it.
  - Templates and locales share one `RegisterSourceOutput`, so editing one .json re-emits every
    template and vice versa (pre-existing shape; incremental win currently nil).
  - `users.locale` is unbounded free text by design (unknown degrades to English); whatever
    preference-setting endpoint lands later must validate against `Locales.All`.
- Strongly typed IDs (deferred from spec 005 scoping, 2026-08-02): `readonly record struct`
  value objects (`UserId`, `BookmarkId`, …) through both apps. Zero runtime cost, but gated on
  JauntyQ converter support for the wrapper types, and low urgency here: ownership-scoped SQL
  already turns a swapped id into a 404, so today's failure mode is a silent no-op, not a leak.
- Analyzer risk pair (from V1 review, 2026-07-31): `TreatWarningsAsErrors` with unpinned
  `AnalysisLevel=latest-recommended` means an SDK bump can hard-break the build on new rules;
  consider pinning the level or adding a `WarningsNotAsErrors` escape hatch before the cutover.
- ~~.editorconfig naming rules cover interfaces/types/members only — private fields, constants,
  locals and parameters are unenforced (V1 review note).~~ **Done 2026-08-18**,
  `chore/editorconfig-naming-rules`: rules added for const fields and static readonly fields
  (PascalCase), private fields (`_camelCase`), locals and parameters (camelCase). The measurement
  the user asked for first: **one violation across the whole solution** — a local const named
  `Yaml` in `ActionPinTests` — so the rules went in enforcing rather than advisory. The real find
  was that none of the naming rules ran at all: IDE1006 has to be enabled by
  `dotnet_diagnostic.IDE1006.severity`, and without it a rule's own severity is decorative, which
  is why `interface_should_be_begins_with_i` had been silent since the file was written.
  `EditorConfigNamingTests` guards both that line and dangling rule references. Not measured:
  `skeleton/`, which is not in `NativeWeb.slnx` and cannot build unstamped.

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- BEM rename (`feature__title` -> `feature-title`/`feature_title`): costed 2026-08-02 (~40
  names / 205 occurrences / 23 files + three-repo sweep); user decided 2026-08-02 to keep BEM.

- Unused vendored `webauthn-ceremony.js` in manzil.my: removed 2026-08-03 (`1d795e1`). Not
  needed by the eventual epass-backed auth either — WebAuthn credentials are RP-scoped, so the
  ceremony runs on the IdP's own pages and a delegating relying party never calls
  `navigator.credentials` itself.

- IdP passphrase reset showing a blank page with scripting off (H5 nit, 2026-07-31): already
  fixed by `0ba2d04`, the spec 004 no-JS retrofit — `Redirects.To` returns a real redirect when
  `HX-Request` is absent. Nothing asserted it, because every test in the file sent `HX-Request`;
  `ResetStart_Then_Verify_WithoutScripting_RedirectsAtEveryStep` now holds both steps in place.

- `InternalsVisibleTo` for the DevMarks unit tests: done in manzil.my 2026-08-03 (`e299972`).
  The grant already existed in `apps/manzil/AssemblyInfo.cs`; only the reflection and the
  comments claiming otherwise were left, and both are gone.

- `devmarks_dev` migration drift on 0006: not a code defect. `manzil_dev` has 0001-0006 all
  recorded and `search_match` present, and nothing in either repo references `devmarks_dev` any
  more — it is the pre-rename database, superseded by the extraction. Making 0006
  `CREATE OR REPLACE` was rejected: 0002-0005 are equally non-idempotent by design, and the
  runner's `schema_migrations` tracking is what makes re-runs safe, so a lone idempotent
  migration would only mute the divergence signal the others keep. Dropping the stale database
  is a cleanup step instead — `scripts/cleanup/devmarks-app.ps1 --execute --drop-database`.

## Deferred from V4 review (2026-07-31)
- Per-field errors: endpoints surface validation failures as page-level banners (parity with the
  old markup); form-field.html's Error/aria-invalid path is ready but every caller passes
  Error="". Thread real per-field errors when the UX is revisited.
- Fragment-assembled sentences in bookmarks/index.html empty states and bookmark-row's
  "Saved {date}" fix English word order across locales; replace with parameterized keys +
  precomputed C# strings.
- ~~Results.Redirect on auth-expired paths ignores HX-Request across all endpoint files~~ —
  **done 2026-08-17**, `fix/idp-correctness-backlog`. One site was live: `PostVerify` in
  `TwoFactorEndpoints` returned a bare 302 when the TOTP challenge cookie had expired, and that
  form opts into htmx, so the sign-in document was swapped into the 2FA card on the most ordinary
  failure the endpoint has. The other eight were latent — every `data-nw-*` element in the app is
  a `method="post"` form, so no GET is htmx-driven today, but `boot.js` derives `hx-get` from any
  anchor carrying the attribute, so one added link would make them live. All now route through
  `Redirects.To`. The note's "RedirectResult's comment says 303 but emits 302" was stale: there is
  no `RedirectResult`, and `Redirects.To`'s comment says 302 and emits 302. The separate 302-vs-303
  question above is still deliberately unchanged.
- ~~ValidateAsync (4 files) checks antiforgery before HasFormContentType, so a non-form POST
  gets 400 not the intended 415 (pre-existing).~~ Fixed 2026-08-17 — see the decided entry
  above. It was seven files and 17 handlers, not four.
- Anonymous pages (home, /u/*) mint an antiforgery cookie via Chrome.For/GetAndStoreTokens,
  making the crawlable pages uncacheable (pre-existing convention) — revisit in V7 perf work.
- Home route sits outside the "public" rate-limit group (pre-existing) — revisit in V5/V6.
- "1 tags" on /tags (tags.count has no singular variant) and count_saved shown for
  tag-filtered lists (both parity with old markup) — wording pass later.
- Nav shows Bookmarks/Tags/Import to signed-out visitors and never marks Settings active
  (both parity with the old layout) — UX pass later.
- ~~GenericFailureMessage const duplicates error.signin_generic for the integration tests;
  point the tests at Locales.Get("en") and drop the const.~~ Not this repo — checked
  2026-08-17 and neither `GenericFailureMessage` nor `error.signin_generic` exists anywhere
  here. Like the rest of this V4 block it belongs to manzil.my, which is now its own
  repository; carry it there or drop it.
- Single-page lists render a pagination landmark with only position text (parity).
- tools/convert-*.py are the R42 conversion scripts, kept as reference for the IdP template
  migration spec; delete with that spec's close-out.
- ~~(V6 review, fable) apps/web stores the session cookie value as the bff_store row's
  plaintext UUID PK, so any DB read yields live bearer tokens; devmarks stores only the
  SHA-256 of its token. Hash the IdP's at rest the same way~~ — **done 2026-08-16**, spec 007
  (`docs/specs/007-session-token-at-rest/`). Rows are keyed by `SessionToken.StorageId`;
  migration 0054 converted the existing ones in place, so no session was invalidated.
- (V6 review, fable) devmarks has no DataProtection key persistence, so every restart
  invalidates outstanding antiforgery tokens; mirror web's PersistKeysToFileSystem block.
- (V6 review, fable) devmarks loads user-secrets unconditionally rather than
  Development-only (web gates it); align.
- ~~(V8 review, opus) robots.txt/sitemap.xml hardcode the production hosts (epass.io,
  manzil.my) and are served verbatim in every environment, so staging advertises a
  production Sitemap URL~~ — **closed 2026-08-27, spec 011 T1/T2.** Both static files are
  deleted; `apps/web/SiteMetadataEndpoints.cs` renders them from
  `NativeWeb:CanonicalBaseUrl`, and unset falls back to the request's own scheme and host, so
  staging advertises staging. The skeleton's copies were never a hardcode — they carry the
  `__product__.example` placeholder the stamper rewrites.
- (V8 review, opus) apps/web `/` still serves the temporary "Phase 0 foundation is up"
  landing; ship the real landing before submitting the sitemap anywhere. (The epass.io half of
  this entry is closed — see above; only the landing page remains.)
- (V8) The plan's "`/` → landing where the product wants it" was deliberately deferred:
  both apps keep a dynamic session-aware `/`; manzil.my decides its own static landing
  during the V10 extraction.
- ~~Skeleton e2e process fixture runs its inner `dotnet publish` without the UsePackages flags,
  so a stamped product's CI e2e step stays checkout-mode even once a package feed exists —
  fold into the feed-gap fix (spec 003 R6).~~ **Stale, checked 2026-08-17.** The fixture's
  command line indeed omits them (`__PRODUCT__ProcessFixture.cs:35`), but it does not need
  them: `skeleton/.github/workflows/ci.yml` sets `NativeWebUsePackages` and `JauntyQUsePackages`
  as *env vars* on the e2e step, MSBuild reads bare env vars as properties, and env is
  inherited by the child publish. Same wiring NativeWeb's own e2e step got on 2026-08-03.
  (Superseded 2026-08-17: those two env vars are gone. The mechanism is unchanged and is now
  what the whole workflow rests on — `NativeWebRoot`/`JauntyQRoot` at workflow level.)

- ~~**The skeleton has the gap NativeWeb just closed, one level up, and it needs a decision.**
  `libs/*` here are all `IsPackable` with `NativeWeb.*` package ids, and the skeleton builds
  against them with `-p:NativeWebUsePackages=true` — but this repo has no `publish.yml`
  (only `ci.yml`), so nothing has ever pushed a `NativeWeb.*` package anywhere, and the
  skeleton has no `nuget.config` to find one with. It is exactly the shape fixed on
  2026-08-17 for JauntyQ, and it is why the skeleton's suites have never run. Not a fix:
  it needs a call on whether NativeWeb versions and releases itself independently
  (jauntyq's `publish.yml` on `v*` tags is the pattern to copy), which is a release-process
  decision rather than a wiring one.~~ **Done 2026-08-17**, `chore/skeleton-local-mode-ci`,
  and it turned out not to need the release decision at all: CI now builds in **checkout
  mode**, checking `extrode-legacy-org/NativeWeb` and `extrode-legacy-org/jauntyq` out as SIBLINGS of the product
  (which moves into `product/`) and pointing `NativeWebRoot` / `JauntyQRoot` at them as
  workflow-level *environment* variables (one
  definition reaches `dotnet list package`, which takes no `-p:`, and the e2e fixture's child
  publish). The skeleton also now ships the missing `nuget.config`. The publish-vs-not
  decision is deferred, not answered — package mode still exists, still has no feed, and is
  simply no longer what CI depends on. Requires a `FRAMEWORK_CHECKOUT_TOKEN` secret on each
  stamped product repo, since both framework repos are private.
- **Neither framework checkout in `skeleton/.github/workflows/ci.yml` is pinned to a tag** —
  both use `ref: dev`, because neither repo publishes tags (NativeWeb has no `main` at all).
  Until they do, a push to `dev` in either framework repo can turn every stamped product's CI
  red with nothing having changed in that product. Pin to tags when the first release lands.
  Raised 2026-08-17.
- ~~`.gitignore` does not ignore `tmp/`~~ — done 2026-08-17, `feat/enable-telemetry`. Escalated
  from warning to error the moment that branch put a throwaway NativeAOT publish there: 159
  scratch files exposed to `git add -A` on the commit about to be made.
- ~~V9 review findings that are pre-existing on dev~~ — done 2026-08-03,
  `chore/v9-pre-existing-findings`. (a) root `Directory.Build.props` dead `JauntyQUsePackages`
  auto-default deleted (it could never fire: the probe above it always assigns `JauntyQRoot`)
  and `EnsureJauntyQRoot` now probes the generator csproj, so an empty checkout dir fails at the
  gate rather than as CS0246. (b) `AppProcessFixture` drains stdout/stderr and keeps a 200-line
  tail, which the startup `TimeoutException` now reports. (c) `ilammy/msvc-dev-cmd` pinned to
  `0b201ec74fa43914dc39ae48a89fd1d8cb592756`, and the clang/zlib1g-dev install landed after all
  — it is not the (extracted) devmarks job that needs it but the surviving `test` job's e2e
  step, whose fixture runs an inner NativeAOT publish on the Linux runner. That same step now
  sets `JauntyQUsePackages` as an env var, because `$DOTNET_BUILD_ARGS` reaches only the outer
  `dotnet test` and MSBuild reads bare env vars as properties. Both copied from
  `skeleton/.github/workflows/ci.yml`, and both were **unverifiable until the CI package-feed
  gap was resolved** — that gap closed 2026-08-17 (`fix/ci-package-feed`), so the next push
  is the first run that can actually exercise them. Still unverified until one goes green,
  and it needs the org package-access grant to be in place first. (d) `/readyz` is bounded by `ReadinessCache` (1s, `NativeWeb:Readiness:CacheSeconds`)
  rather than by a rate limit: partitioning is per-IP, and until `ForwardedHeaders:KnownProxies`
  is configured every client shares the proxy's partition *including the load balancer's own
  probe*, so a limiter would have let anyone spend the budget and make the next real probe 429
  — a remote off-switch for a healthy instance. A test pins that neither probe is rate limited.
  **Still open, in manzil.my:** its `/` endpoint has no rate-limit policy and its `/readyz` is
  the same unbounded DB round trip; `ReadinessCache` is the shape to pattern-copy.
- ~~V10 removal review: `Directory.Packages.props` pins `Microsoft.AspNetCore.OpenApi` 10.0.7 and
  `Microsoft.OpenApi` 2.7.5 with no remaining consumer in any csproj (the transitive-override
  comment is now false); removing pins counts as dependency removal, so left for a confirmed
  cleanup. Also trivial: `check-publish-budgets.ps1` `-Port` default 5999 is unreachable (sole
  caller passes 5998).~~ Both done 2026-08-17 (`chore/test-and-skeleton-hygiene`), the pin
  removal on explicit approval. The port default is 5998 now, matching ci.yml, and
  `PublishBudgetScriptTests` fails if the two ever drift again.
- Dev-loop bug (found 2026-08-01, applies to any app using MapStaticAssets + fingerprinted CSS,
  so manzil.my and apps/web alike): under `dotnet run` in Development,
  `StaticAssetDevelopmentRuntimeHandler` resolves the precompressed gzip variant to
  `wwwroot/<name>.css.gz` (publish-only file), throws FileNotFoundException, and the browser
  request path swallows it into an empty 200 with no Content-Type — `nosniff` then blocks every
  stylesheet, so `dotnet run` pages render unstyled. Direct `.gz` route 500s with the real
  exception. Workaround: run the published Release output (serves correctly). Investigate
  whether it is a known .NET 10 issue or triggered by our pipeline ordering.
- spec 004 review note: consider dropping the `antiforgeryField = ""` / `botTraps = ""` defaults on Render*Form/Fragment helpers so the compiler catches an omitted token field (SettingsEndpoints.cs:119 regression class); ~140 unit-test call sites would need updating. Also pre-existing: enumeration-defense StripVaryingTokens blanks every content="..." attribute, wider than the token meta.
- integration: SettingsEndpointsTests fail 11/13 when run with --filter in isolation (TrapForm static canary fallback GETs /auth/signin with an authenticated client); full-suite runs pass. Pre-existing order dependence, verified at B4 HEAD 0ba2d04.
- formatter (2026-08-02 review findings, non-blocking): (a) the AmbiguousLineStart guard only
  accepts a literal "\n" next — a CRLF checkout (no .gitattributes LF force on *.html) bails
  every banner-guard template silently; accept "\r\n" too. (b) depth sets keep entries from
  callers that later bail (CascadeBails never prunes them), which is why MaxDepthVariants had to
  go 4->8 and up to ~5 dead RenderAtD overloads per hot partial ship; recomputing depth sets
  over surviving callers after each cascade would make 4 sufficient again. (c)
  bookmarks/index.html and public/profile.html have never formatted: a {{for}} body ending in a
  partial splice doesn't return to its start state; supporting that shape would format the two
  remaining manzil pages.
- ~~`tests/nativeweb.web.tests.e2e/AppProcessFixture.cs` never drains the redirected stdout/stderr~~ — done 2026-08-03, `chore/v9-pre-existing-findings`, same shape as manzil's spec 006 T9 fix. Worth carrying back to the skeleton and to manzil: both discard the drained output (`static (_, _) => { }` / no tail in the timeout message), so a startup failure there still reports nothing but the elapsed 60 seconds.
- ~~CSP hash generator, remaining findings from the 2026-08-03 review~~ — **all three done**.
  (a) comment / raw-text / attribute-value awareness, and (c) the app-level lockstep that
  recomputes from the served response, landed 2026-08-16 in `fix/csp-hash-scan-context`
  (`docs/plans/2026-08-16-003-csp-hash-scan-context.md`). (b) the `IsExecutableScript` MIME
  allowlist landed 2026-08-17 in `fix/loose-ends`: it now carries the HTML standard's whole
  JavaScript MIME type set, so `text/jscript`, `application/x-javascript`, `text/livescript`,
  the `text/javascript1.x` versioned spellings and the `x-` variants are hashed rather than
  silently blocked. `templates/csp/legacytypes.html` pins it, with a `text/template` control in
  the same fixture that must stay unhashed.
- ~~Skeleton templates are CRLF in the working tree although `.gitattributes:15` says
  `*.html text eol=lf`; the blobs are LF, so the two disagree. `git add --renormalize` would fix
  it but touches every template, so it is its own change.~~ **Stale — measured 2026-08-24, there
  is nothing to fix.** `git ls-files --eol` reports `i/lf w/lf attr/text eol=lf` for every
  skeleton `*.html`: index and worktree agree, and both agree with the attribute. The
  `--renormalize` this entry proposed would have rewritten every template for no change. Whatever
  produced the CRLF worktree copy was undone before the entry was written or by a later checkout.
  Still true, and worth keeping: the CRLF/LF disagreement is why the skeleton once emitted
  browser-mismatched CSP hashes, and the hash generator's normalization is what makes that
  unable to recur.
- ~~The skeleton's integration and e2e suites have never been run: they only exist inside a stamped
  product, which needs its own database. That is why `SmokeTests` kept asserting a nonce in the
  home page CSP for the whole of spec 006 (fixed 2026-08-03 by inspection, still unexecuted).
  Worth a CI job that stamps, creates a throwaway DB, and runs all three layers.~~ **Done
  2026-08-25**, `feat/ci-skeleton-and-scaffold`: the `skeleton-stamp` job stamps a `Ciprobe`
  product with `tools/new-product.ps1`, provisions `ciprobe_dev`, migrates, and runs all three
  layers (47 unit / 7 integration / 2 e2e locally). The whole path was verified end to end on
  this machine before any YAML was written. Two findings came out of it:
  - ~~`tools/new-product.ps1` copies the skeleton's `bin/` and `obj/` into the stamped product, so
    a product stamped from a built checkout starts life carrying the skeleton's build output.~~
    **Fixed 2026-08-25**, `fix/new-product-excludes-build-output`: the enumeration is now
    `git ls-files --cached --others --exclude-standard`, so the stamped set is what git tracks
    plus what `skeleton/.gitignore` does not exclude — 168 files, not 714. Worse than the size:
    the old enumeration would have carried a `.env`, a `*.pfx` or `secrets/` into a new product's
    *first commit*, since `git add -A` runs right after the copy and the ignore file was the only
    thing that had ever excluded any of it. `NewProductStampTests` runs the script's own dry run
    and asserts set equality against git in both directions, so a "no build output" regression
    and a "silently stamps nothing" regression both fail.
  - `PackageFeedConfigTests.Workflow_AuthenticatesTheSourceNameNuGetConfigActuallyDeclares` had
    to widen to the union of `nuget.config` and `skeleton/nuget.config`: the stamp job
    authenticates `framework-github`, which only the skeleton's config declares.
- The same CI branch found that **CI was running none of the scaffolding tiers** — not the
  generator's unit tests, not the integration tier, not the e2e tier — a second gap beyond the
  skeleton one. All four now run in the `test` job, the e2e tier under `JauntyQUsePackages`.
- `Directory.Build.targets` disables precompressed static-asset generation in Debug as a
  workaround for a framework defect: `StaticAssetDevelopmentRuntimeHandler` resolves compressed
  variants against `wwwroot` instead of the manifest content roots, so a gzip-accepting client got
  a zero-length body. Remove the property once the SDK resolves the variant correctly. The Debug
  build no longer exercises compressed-asset serving at all, so only `dotnet publish` covers that
  path — **the Release smoke check now exists** (2026-08-18, `chore/release-compressed-asset-check`):
  `CompressedAssetTests` in the e2e tier, against the published binary the fixture launches, asserts
  a gzip-accepting client gets `Content-Encoding: gzip`, a body smaller than the identity response,
  and one that gunzips to exactly that response. What remains is removing the workaround itself.
- manzil.my needs the same `Directory.Build.targets` workaround; it has the identical defect.
- ~~A fresh **production** database has the same first-party-application gap CI hit.~~ **Closed
  2026-08-17** by `db/migrations/0055_first_party_application.sql`: every deployment now gets the
  row, guarded on "realm 1 has no applications" so existing deployments neither duplicate it nor
  gain a dead row. The secret is generated with `gen_random_uuid()` inside the migration and
  never recorded, which is safe because `ResolveFirstPartyAsync` matches on `client_id` and reads
  `app.Id` without ever consulting `secret_hash`. Measured: every migration but 0055 applied gives
  33/258 integration failures, the exact CI set; with 0055 it is 258/258.
- ~~`db/seed/ci/` and the `Apply CI seed` step in `ci.yml` are now redundant.~~ **Done
  2026-08-17**, `chore/retire-ci-seed`: the workflow step is gone, `CiSeedTests` was rewritten to
  assert ci.yml applies no seed at all (re-adding one would make CI pass while a fresh production
  database stayed broken), and the seed file itself is left to
  `scripts/cleanup/ci-seed-retirement.ps1` for the user to run.
- ~~`RealmResolver` throwing an `InvalidOperationException` for missing *configuration*.~~ **Done
  2026-08-17**, `fix/realm-resolver-consistency`. `ResolveFirstPartyAsync` now returns null and
  both call sites answer 503, the same status an unconfigured connection string or an absent
  `PairwiseSubjectDeriver` already gives, so one class of problem has one status. A boot-time
  check (`StartupConfigGuards.FailStartupIfFirstPartyApplicationMissing`) refuses to start outside
  Development when the database answers and the row is absent.

  The check is deliberately three-valued. Anything that stops it *reaching* the database --
  unconfigured, unreachable, unmigrated, wrong credentials -- is `Indeterminate` and does **not**
  fail startup: refusing to boot because Postgres is not up yet breaks an ordinary rolling deploy,
  and `/readyz` already reports that state far better than a dead process does. "Cannot tell" must
  never be reported as "wrong". Only `Missing` exits.
- ~~`skeleton/.github/workflows/ci.yml` applies migrations only, so a stamped product carries the
  same gap.~~ **Not a gap -- closed 2026-08-17.** Checked while pinning the action above: the
  skeleton has no `RealmResolver`, no first-party application and no `db/seed/` at all, so there
  is no row to seed. More to the point, migrations-only is now the *correct* posture rather than
  an oversight: NativeWeb resolved its own version of this by moving the row into migration 0055
  and retiring `db/seed/ci` entirely, so the skeleton was already doing the right thing. Copying
  a seed step across would have propagated the mistake, not the fix.
- ~~`ActionPinTests.ThirdPartyActions_ArePinnedToASha` scans workflows for the `uses:` key without
  skipping comments~~ — **done 2026-08-18**, `fix/action-pin-comment-scan`. The pattern is
  anchored to the start of a line with only whitespace and an optional `- ` sequence marker
  allowed before the key, which is what excludes `#`. Nine tests: four commented shapes that must
  yield nothing, four real step shapes, and one document carrying both. The comment reworded
  around it in `skeleton/.github/workflows/ci.yml` can now say whatever it likes.

## Solution-wide `dotnet test` hangs, 2026-08-30

`dotnet test` with no project argument produced no output in 30+ minutes and was killed. Every
one of the eleven test projects passes when run individually, in well under a minute each:
**699 web-unit, 333 web-integration, 73 web-e2e, 184 signin-unit, 157 render-unit, 64
scaffold-unit, 34 signin-integration, 31 scaffold-integration, 16 sample-unit, 13 scaffold-e2e,
3 signin-e2e — 1,607 total, all green.** Suspects, in order: the three integration tiers
contending for the same PostgreSQL instance when run in parallel, and
`nativeweb.signin.epass.tests.aot`, which is a publish check rather than a test project. Not
investigated; run projects individually until it is.

## Security audit findings, 2026-08-19 (`review` on Opus + `glm-think` on GLM-5.3)

**Worked through 2026-08-31 on `fix/security-audit-2026-08-19`. One item is left open**, the
state-changing `GET /oauth/authorize`, because every fix for it is a design change. Of the rest,
seven were fixed on that branch and six turned out to have been fixed by earlier sessions that
never struck them through — which is the reason each closed line below carries its evidence
rather than a tick. The backlog was stale; verify before fixing.

Two independent audits of the `apps/web` request-handling surface, run after the antiforgery
400/500 fix. Recorded, not fixed — each needs its own branch and a decision on priority. Both
audits independently derived the structural point the T1.2 spike measured: **endpoint filters run
after argument binding**, so `RequireForm()` and `RequireServices<>()` cannot guard a `[FromForm]`
bind. That is why the antiforgery fix is middleware. `RequireServices<>()`'s 503-degrade has the
same ordering flaw and is still unaddressed.

**Measured 2026-08-30** (`fix/require-services-coverage`), which narrows that last sentence: the
503-degrade's ordering is real but benign on both routes carrying it, because argument binding
rejects first. A JSON POST to `/auth/register` with all three gated services removed answers
**415, not 503** — the RDG's own `HasFormContentType` check fires during the scalar `[FromForm]`
bind, upstream of every filter. Two consequences, both now pinned by
`RequireServicesFilterTests` (10 facts; the filter had none):
- `RequireForm()` is **redundant on both routes it is applied to** — removing it left all six
  `ContentTypeGuardTests` cases passing. Kept as defence in depth for a route bound in a shape
  whose binder does not check; the comment claiming it produced the 415 is corrected.
- The degrade cannot mask the antiforgery check: `AntiforgeryValidationMiddleware` is upstream of
  binding, so an unconfigured box still answers 400 to a tokenless form POST, asserted directly.

**Converged (both audits):**
- ~~`apps/web/Auth/SignInEndpoints.cs:39` — `POST /auth/signout` is the only auth-surface form POST
  with no `.RequireRateLimiting()` and no content-type guard, reachable unauthenticated, and does
  a `bff_store` delete per request. Antiforgery tokens are free from any GET, so the token is no
  throttle.~~ — **already closed; verified 2026-08-31.** The route carries
  `.RequireRateLimiting("auth")`, and `RateLimitCoverageTests` now asserts the property
  over every mapped route rather than the list, so the next unlimited route fails the
  build.
- ~~`apps/web/Program.cs:428-431` — `AddAntiforgery` pins only `HeaderName`. The antiforgery cookie
  keeps framework defaults: no `__Host-` prefix, `SecurePolicy` left at `SameAsRequest`. It is the
  one cookie in the app not pinned Secure, and the other half of the CSRF pair.~~ —
  **already closed; verified 2026-08-31.** The cookie is `__Host-nw-antiforgery`,
  `SecurePolicy.Always`, `SameSite.Lax`.

**Enumeration oracles** (the register flow never got the defences the reset flow engineered).
**All three closed; verified 2026-08-31** — the email branch pays the same Argon2id cost
and returns the identical redirect, the code send is backgrounded exactly as the reset flow
does it, and register-verify answers `genericError` with no distinct too-many-attempts copy.
The username-taken message is the one that stays, and stays deliberately: a registration form
that cannot say a username is taken cannot complete a registration. The timing half of that
one is closed — the branch returns before `Passphrases.Hash`, but every branch that returns
early now pays the same hash.
- ~~`RegisterEndpoints.cs:173`, `:182` — "That username is already taken." for both a `users` row
  and a pending `temp_users` row: a direct, unauthenticated account-existence oracle on the same
  identifier used to sign in. Returns *before* `Passphrases.Hash`, so it is distinguishable by
  timing as well as by body. Contradicts the email defence 40 lines below at `:205-225`.~~
  (the message itself stays; see above)
- ~~`RegisterEndpoints.cs:238` — the genuine-registration branch awaits `codeSender.SendAsync` (SMTP)
  and `breachChecker.CheckAsync` (HIBP, up to 2s) while the already-registered branch pays only a
  dummy Argon2 hash. `PassphraseEndpoints.cs:254-264` backgrounds the send precisely to avoid this;
  register did not get the same treatment. Latency is the oracle.~~
- ~~`RegisterEndpoints.cs:296` — register-verify returns a distinct "Too many attempts" message only
  when a `temp_codes` row was staged, i.e. only when the email is *not* already registered.
  `PassphraseEndpoints.cs:401-411` fixed this exact bug on purpose.~~

**Correctness / functionality:**
- ~~`SecurityHeadersMiddleware.cs:57-61` vs `OAuthEndpoints.cs:404-405` — the CSP rationale claims
  consent POSTs never redirect cross-origin via a 302. False: `Redirects.To` returns a real 303 to
  the cross-origin `redirect_uri` for a request without `HX-Request` (`Redirects.cs:45-60`).
  Chromium enforces `form-action` against redirect targets, so the no-JS consent path
  `OAuthAuthorizePage.cs:14-18` deliberately supports is blocked there. Either widen the CSP to the
  registered redirect origins or replace the comment with an honest statement of the tradeoff.~~
  — **done 2026-08-31**, `fix/security-audit-2026-08-19`. Widened, not documented away: the
  consent screen sets `SecurityHeadersMiddleware.FormActionItemKey` to the origin of the
  redirect_uri it has already matched against the application's registered list, so the header
  reads `form-action 'self' https://client.example`. The origin, never the path. Two facts —
  the consent screen widens, an ordinary page stays at `'self'` — and two mutations, one
  dropping the widening and one naming the whole URI instead of its origin.
- ~~`SignInEndpoints.cs:49` — bare `Results.Redirect("/")` instead of `Redirects.To`, which is the
  exact case `Redirects.cs:14-18` calls out: htmx chases the 302 by XHR and swaps a whole document
  into a fragment slot. Latent until an anchor carries `data-nw-*`.~~ — **not a defect;
  verified 2026-08-31.** The one surviving `Results.Redirect("/")` in sign-out is inside an
  `if (!Headers.ContainsKey("HX-Request"))` branch and the htmx path above it sets
  `HX-Redirect` itself, which is what `Redirects.To` would have done. The no-JS escape hatch on
  the passwordless change page is why the plain-redirect arm has to exist at all.

**Cookie and rate-limit consistency:**
- ~~`SignInEndpoints.cs:400`, `WebAuthnEndpoints.cs:437`, `ReturnUrlAuth.cs:23` — challenge and
  return-url cookies use `Secure = !environment.IsDevelopment()`, while the session cookie
  (`SessionAuth.cs:89-96`) pins `Secure = true` unconditionally on the stated grounds that browsers
  treat `http://localhost` as a secure context. The gate buys nothing and is a way to lose the flag;
  these two carry pre-session step-up bearer tokens.~~ — **already closed; verified
  2026-08-31.** No `Secure = !` gate survives anywhere under `apps/web`.
- ~~Full list of mapped routes with no `.RequireRateLimiting()`: `Program.cs:728` (`GET /`, known),
  `:713`, `:717`, `:722`, `SignInEndpoints.cs:32`, `RegisterEndpoints.cs:37`, `:46`,
  `TwoFactorEndpoints.cs:30`, `:39`, `WebAuthnEndpoints.cs:37`, `:46`, `PassphraseEndpoints.cs:43`,
  `:50`, `:57`, `SettingsEndpoints.cs:23`, `AdminEndpoints.cs:25`, `SecurityTxtEndpoint.cs:21`,
  `:22`. `/readyz` (`:803`) is a documented exclusion. The unauthenticated page-rendering GETs each
  mint a data-protected antiforgery token per request and are the cheapest amplification surface in
  the app; `GET /auth/webauthn/verify` also issues two DB queries once a challenge cookie is present.~~
  — **already closed; verified 2026-08-31.** `RateLimitCoverageTests` walks the live
  `EndpointDataSource` and fails on any route with no `EnableRateLimitingAttribute`, against a
  six-entry allowlist that each carries its reason. The property, not the list.

**Lower, recorded for completeness:**
- **OPEN — needs a decision.** `OAuthEndpoints.cs:38`, `:180-187` — `GET /oauth/authorize` is a state-changing GET and the
  session cookie is `SameSite=Lax`, so it is sent on cross-site top-level navigation. Where
  silent-SSO conditions hold, a cross-site link can drive a signed-in user into a grant with no
  consent interaction. PKCE does not help; the attacker is the client. Conditional on realm config.
  Left unfixed 2026-08-31: every fix is a design change (force an interstitial, drop silent SSO,
  or move the grant behind a POST), and which one is right depends on what silent SSO is for.
- ~~`AdminEndpoints.cs:484-510` — `imageUri`, `website`, `companyWebsite` validated by length only,
  never parsed as URIs, and `imageUri` is emitted as `<img src>` on the public consent screen.
  `default-src 'self'` blocks the load today, so this is stored junk rather than XSS — one CSP
  change away from mattering.~~ — **done 2026-08-31**, `fix/security-audit-2026-08-19`.
  `ValidateHttpUrl` parses all three: absolute, http or https only, cleartext accepted for a
  loopback host and nowhere else, no `user:pass@`. 17 unit facts (`javascript:`, `data:`,
  `file:`, a relative path, embedded whitespace and newline) plus an integration fact asserting
  a non-http `imageUri` is rejected AND that the create stores nothing.
- ~~`Layout.cs:69` — `{{antiforgeryToken}}` interpolated raw into a `<meta content="...">`, the only
  unencoded request-derived interpolation in the Render layer. Safe because the token is base64url,
  i.e. safe by a framework charset rather than by the encoder every sibling uses.~~ —
  **done 2026-08-31**, `fix/security-audit-2026-08-19`. Encoded like every other interpolation
  in the file, which changes no byte of today's output — the point being that it is now held by
  the encoder rather than by someone else's choice of alphabet. Integration fact swaps in an
  `IAntiforgery` returning a token that closes the attribute and opens a `<script>`.
- ~~`PassphraseEndpoints.cs:295`, `:331` — `maskedDestination` round-trips through the client
  unvalidated and is reflected into "We sent a code to {X}" on an unauthenticated reset page.
  HTML-encoded, so content spoofing rather than XSS — on the one page whose job is telling the user
  where a takeover code went. It is server-derivable from `hashedUsername`.~~ —
  **done 2026-08-31**, `fix/security-audit-2026-08-19`. Signed, not server-derived: deriving it
  from `temp_codes.sent_to` would build a NEW enumeration oracle, because a real account's mask
  would change the moment its code expired while an unknown identifier's never would. An
  `ITimeLimitedDataProtector` over a 24h window (same shape as `BotTraps.cs:181`) carries it
  instead, and a token that does not verify drops BOTH the displayed text and the hidden field —
  the first attempt fell back on the display and still echoed the attacker's string into the
  form. Two integration facts and two mutations.
- ~~`WebAuthnEnrollPage.cs:44`, `PassphraseChangePage.cs:114`, `WebAuthnVerifyPage.cs:36` — WebAuthn
  options JSON interpolated raw into `<script type="application/json">`, carrying a user-chosen
  `username` with no charset restriction. Safe only because Fido2NetLib's `ToJson()` uses
  System.Text.Json's HTML-escaping encoder — asserted in a comment, pinned by no test.~~ —
  **done 2026-08-31**, `fix/security-audit-2026-08-19`. Pinned by four facts, one on the library
  itself: a username of `</script><img src=x onerror=alert(1)>` serializes with `<` and
  `>` and no raw angle bracket, and each of the three pages still renders exactly two
  `<script` and two `</script>`. The library fact is the one that matters — a Fido2NetLib
  release switching to the relaxed encoder would have turned a username into markup with every
  suite green. The inert text (`onerror`) survives verbatim; only the brackets are escaped.
- ~~`CspReportEndpoint.cs:27-51` — no content-type guard and no origin check; `Sanitize` at `:59`
  neutralises CR/LF and C0/C1 so the residual is log noise only.~~ — **done 2026-08-31**,
  `fix/security-audit-2026-08-19`. Content type gated to `application/csp-report` and
  `application/reports+json`, parameters stripped, anything else 415. The origin check was
  deliberately NOT added and the reason is in the file: `report-uri` sends no `Origin` at all
  and `report-to` sends the violating document's, which is `null` under a sandbox. 13 unit facts
  including `application/csp-report-not` and `xapplication/csp-report`, so the check cannot be a
  `StartsWith` in disguise.
- ~~`Program.cs:642-650` — `StaticPagesMiddleware` and `UseRouting` sit outside `ErrorPageMiddleware`,
  so an exception in the clean-URL rewrite or in route matching bypasses both the error shell and
  the security headers.~~ — **done 2026-08-31**, `fix/security-audit-2026-08-19`. Both
  `ErrorPageMiddleware` and `SecurityHeadersMiddleware` moved above `StaticPagesMiddleware` and
  `UseRouting`. The headers survive the move because they are written from an `OnStarting`
  callback, not on the way in, so the static-page marker set downstream is still readable when
  the callback runs. Guarded by `PipelineOrderTests`, which asserts registration order in the
  source — no request that never throws inside the rewrite can see the difference, so a
  behavioural test here would assert nothing.

## Scaffolding POC follow-ups, 2026-08-19

- ~~`AntiforgeryValidationMiddleware` is duplicated: `apps/web/Middleware/` has the real one and
  `apps/sample/Program.cs` inlines a copy.~~ — **half done 2026-08-19**,
  `feat/scaffold-host-extensions`. The sample's copy is gone, replaced by
  `app.UseAntiforgeryRejection()` in `libs/nativeweb.scaffold`, composed into `UseScaffolding()`
  alongside authn/authz/antiforgery. **`apps/web` still has its own** and was deliberately not
  touched: it is a different implementation with its own tests, and moving a live IdP onto a new
  middleware is a change that wants its own branch and its own review. Do that when
  `apps/web` next needs work in that area.
- ~~`apps/sample`'s `IScaffoldStore<T>` is in-memory, which contradicts the
  JauntyQ-for-all-data-access rule.~~ — **done 2026-08-25**, `feat/scaffold-jauntyq-store`.
  `JauntyArticleStore` satisfies the same six methods with JauntyQ queries over PostgreSQL, and
  `ConnectionStrings:Sample` selects between the two: unset keeps the in-memory store, so the demo
  and the whole suite still run with no database. `Tag` stays in-memory deliberately — two models
  behind one seam with two entirely different implementations is the claim, and it is only visible
  if they actually differ. Schema lives in `db/sample/`, apart from the identity schema; see that
  README for why and for the local setup.
  Three things the work turned up, all recorded where they happened:
  - `CreateSlimBuilder` does not see `WebApplicationFactory`'s in-memory config (already known, in
    `NativeWebFactory`) — but `UseSetting` does, and unlike an env var it is per-host, which is what
    lets the in-memory tests keep running in the same process.
  - `DisableParallelization` orders a collection's tests, not its fixtures. Two fixtures publishing
    one project raced ILC; the publish is now shared via `Lazy<Task<string>>`.
  - The read-only timestamp has *two* independent defences — the generator's
    `Build(form, existing)` and `Update.sql`'s SET list — and each hides the other when only one is
    broken. Both confirmed by breaking each in turn; written up in `Update.sql`.
- Hand-written `BindAsync` is a foot-gun the generator avoids and a human would not: nothing warns
  when a `[FromForm]`-free handler loses its antiforgery metadata. An analyzer should flag an
  `IBindableFromHttpContext` type reached from a POST with no `IAntiforgeryMetadata`.
- Scaffolded pages carry no rate-limit policy. `apps/web`'s `RateLimitCoverageTests` would catch
  that in that app; the generator should attach a policy name so it is not per-app vigilance.
- `ScaffoldLayout.Page` embeds its own stylesheet rather than going through `nativeweb.render`.
  Right for a self-contained demo, wrong once a consumer wants its own chrome — `Page` is the seam
  to make overridable.
- ~~No e2e (Playwright) tier for the scaffolding. Unit + integration only.~~ — **done 2026-08-24**,
  `feat/scaffold-e2e-tier`. `tests/nativeweb.scaffold.tests.e2e` publishes `apps/sample` and drives
  the real binary. It exists for one thing the tiers below cannot reach: the generated delete form
  carries `onsubmit="return confirm('Delete this record?')"`, and an `HttpClient` has no JavaScript
  engine, so the integration tier POSTs straight past that guard and would pass identically if the
  attribute were deleted.
- **The scaffolded delete confirm is an inline event handler, so a strict CSP silently removes it.**
  Found while writing the e2e tier. `onsubmit="return confirm(…)"` needs `unsafe-inline` in
  `script-src`, or a hash — `apps/sample` sets no CSP at all, so it works there and the new tier
  passes, but a consumer with `apps/web`-grade headers gets a delete button that deletes on the
  first click with no dialog and nothing in the console to say why. The scaffolding is a library,
  so the consumer's CSP is not ours to choose: the fix is to stop emitting an inline handler.
  Needs a decision on what replaces it (a submit listener from a hashed script the library ships,
  or a confirm step as its own page).
- ~~`ilammy/msvc-dev-cmd` is the last node20 action in either workflow, and there is nothing to
  bump to.~~ — **done 2026-08-20**, `chore/drop-msvc-dev-cmd`. Deleted rather than replaced: the
  ILCompiler locates MSVC itself via its bundled `findvcvarsall.bat`, so the step was redundant,
  and it was exporting `Platform=x64` into the publish path. Both workflows are now node24
  throughout and use no third-party action at all.

## ~~epass.io cutover readiness, probed 2026-08-24~~ — CANCELLED 2026-08-31

**There is no cutover.** `apps/web` and the rest of the identity provider left this repository on
2026-08-31 (`docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`); epass.io keeps its own IdP,
which had diverged from this one anyway. Every blocker below is moot as a NativeWeb item. Left
unstruck line by line because the measurements in it are still true about the code that was here,
and because the `002-epass-cutover-capability-gap.md` note it feeds is the record of why the answer
went the other way.

Seven follow-ups the removal itself created, six of them still live:

- **The framework has no password-policy primitives any more.** `LockoutPolicy`,
  `CommonPasswordChecker`, `HibpBreachChecker` and `BreachChecking` went out with
  `libs/nativeweb.auth`. The skeleton's csproj referenced that library for them and no skeleton C#
  ever used it, so nothing broke — but Django ships password validators and a stamped product will
  want them. Decide whether they come back as a framework lib on the `contrib.auth` side of the
  line, or stay out.
- ~~**`scripts/deploy/` still targets `apps/web`.**~~ **Done 2026-08-31** in `fix/018-deploy-hygiene`
  (plan 007 T12). `build.sh` publishes `apps/sample` by default and takes `PROJECT=<csproj>`; it
  installs the executable under the fixed name `nativeweb`, so `deploy.sh`, `nativeweb@.service`
  and the Caddy files name no project at all. `DeployTargetTests` holds all three to one name and
  refuses a project path anywhere downstream of the build.
- **`docs/02-reference/security-baseline.md` was an ASVS checklist for the IdP.** Reframed rather
  than deleted; someone should decide which of its items are framework posture and which left with
  the application.
- **`libs/epass.auth`, `epass.data`, `epass.mail`, `epass.security` are untracked `obj/`-only
  leftovers** of the 2026-08-19 neutral-naming rename, sitting in `libs/` looking like projects.
  Listed in `scripts/cleanup/extract-idp.ps1`, not removed.
- ~~**`db/migrate.sh` and `db/test-run-lock.sh` are unreachable.**~~ **Retargeted, not retired,
  2026-08-31** in `fix/018-deploy-hygiene`. `MIGRATIONS_DIR` defaults to `db/sample/migrations` and
  takes an override; a missing or migration-less directory is refused before any glob reaches psql;
  the superuser extensions step runs only if `0002_extensions.sql` is there, and when it is not
  `EXT_VERSION` is `none` so the apply loop skips nothing. Kept because the locking design is what
  `skeleton/db/migrate.sh` was derived from. Verified without a database: both guards exit 1.
- ~~**The OTel-under-AOT gate stopped gating.**~~ **Done 2026-08-31** in `fix/018-deploy-hygiene`,
  and the diagnosis in this entry was wrong: `apps/sample` DOES register telemetry, through
  `AddNativeWebWeb` → `AddNativeWebTelemetry`. What broke was the key. The script set
  `NativeWeb__Telemetry__Enabled`, and the sample binds its options from the `Sample` section, so
  the value was never read. Measured on the published binary: `Sample__Telemetry__Enabled=maybe`
  refuses startup with exit 78, `NativeWeb__Telemetry__Enabled=maybe` starts the app normally.
  Fixed with `-ConfigPrefix`, passed from `ci.yml`, held to the app's own section by
  `TelemetryGateTests`.
- ~~**The `Fido2` and `NSec.Cryptography` pins in `Directory.Packages.props` have no consumer.**~~
  **Removed 2026-08-31** on the user's decision, in `chore/drop-webauthn-pins`. Both pins are gone;
  a comment in their place records why the two travel together, so a future reintroduction of
  `Fido2` does not hit the NU1107 libsodium conflict from scratch. Build stayed 0 warnings, 0 errors.

## ~~epass.io cutover readiness, probed 2026-08-24~~ — the cutover half is CLOSED 2026-08-31

**Read `docs/decisions/2026-08-31-008-epass-cutover-closed.md` before acting on anything below.**
The cutover is not a NativeWeb concern and the term is retired here: epass.io built its own browser
surface, and the only NativeWeb-side input left is a push of `dev`, which is the user's.
**What survives in this section is the deploy readiness, which is real and unrelated** — spec 011
T15, the scripts that have never run on a host. Read it as that, not as cutover work.

Probed before scheduling the cutover, on the reasoning that finding unfinished work afterwards is
far more expensive than finding it now. ~~The application side is ready; the deploy side was never
started. Four blockers, none of them code in `apps/web`:~~ **"The application side is ready" was
wrong, and badly — see the capability gap below, added 2026-08-25.** ~~The four deploy blockers are
still real and still unstarted:~~ **Three of the four are addressed by spec 011
(`docs/specs/011-production-deployment/`), merged 2026-08-27 — but "addressed" means written,
not executed. Read each note below for which half survives.**

- ~~**Slice 10's deploy half does not exist.**~~ **Written, never run.** Spec 011 ships
  `scripts/deploy/` — `provision.sh`, blue/green `deploy.sh`, `rollback.sh`, encrypted offsite
  `backup.sh`/`restore.sh`, the systemd units, the Caddyfile and the nftables ruleset — plus the
  runbook at `docs/02-reference/deployment.md`. **No host has ever been provisioned from it and
  no backup has ever been restored** (011 T15, the operator's to run), so the cutover still
  cannot be scheduled against it. `MemoryDenyWriteExecute=yes` in the unit is likewise unverified
  (011 T14). Two further gaps are open by decision: `BACKUP_HEARTBEAT_URL` is unset, so R15 is
  unmet and a backup that stops running is invisible; and one host with nightly dumps accepts up
  to 24 hours of data loss and no failover.
- ~~**No OTLP collector endpoint.**~~ **Enforced rather than closed.** `deploy.sh` refuses to
  promote a release when neither `OTEL_EXPORTER_OTLP_ENDPOINT` nor
  `NativeWeb__Telemetry__Enabled=false` is set, so the silent-retry state can no longer reach
  production by omission — **but nobody has stood up a collector**, and choosing which of the two
  to set is still a decision. Original note kept for the mechanism: telemetry became opt-**out**
  on 2026-08-17
  (`NativeWeb:Telemetry:Enabled` is compared case-insensitively against the literal `"false"`, so
  unset means on — `docs/02-reference/configuration.md:106`, `apps/web/Program.cs:272,303`). A
  production host
  with no `OTEL_EXPORTER_OTLP_ENDPOINT` therefore stands up an exporter and retries against a
  collector that is not there. Point it somewhere or set the flag; do not leave it unset.
- ~~**`wwwroot/sitemap.xml` and `robots.txt:12` already name `https://epass.io`.**~~ **Closed
  2026-08-27, spec 011 T1/T2.** Both files are gone; the two routes render from
  `NativeWeb:CanonicalBaseUrl`, which startup rejects unless it is an absolute http/https origin
  with no path, query or fragment. Unset means each response names the requesting host, so a
  staging host can no longer advertise production. Note the neighbouring hardcode this did **not**
  touch: `apps/web/SecurityTxtEndpoint.cs` still emits `security@epass.io` literally — out of
  011's scope, and correct only after the cutover.
- **CI cannot restore in package mode.** Only `-p:JauntyQUsePackages=true` paths are affected, and
  a deploy pipeline that assumes package mode will not build. ~~**011 sidesteps rather than closes
  it**: `build.sh` pins `-p:JauntyQUsePackages=false` and builds from the local checkout~~ — **that
  sidestep went on 2026-09-05**, because the checkout it named no longer exists to build from
  (`docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md`). `build.sh` passes
  `-p:JauntyQUsePackages=true` now, so the release path restores `JauntyQ.*` like everything else.
  It contains no `dotnet nuget update source` step and neither does any caller, so it works off a
  warm `~/.nuget/packages` and a clean release host gets `NU1101` naming `jauntyq-github`. The
  release is still a workstation task, for that reason rather than the old one.

  **The premise changed on 2026-08-29 and the cause is now narrower.** "No reachable feed
  publishes `JauntyQ.*`" is no longer true: `JauntyQ.Runtime`, `JauntyQ.Generator` and
  `JauntyQ.Schema.Contract` are published under `extrode` at **0.4.0**, matching the pins. What is
  missing is the package-read grant to `extrode/nativeweb`, so restore fails `NU1301`/403 rather
  than `NU1101`. See the entry at the top of Now-ish; that grant is the whole remaining gap, and
  it is one UI action.

Not blockers, verified green the same day: web-unit 494/494, integration 298/298, web-e2e 64/64,
CI run 2026-08-24T07:11Z green on both `aot-publish` and `test`.

### The capability gap nobody had measured, 2026-08-25

**Moved to `docs/architecture/002-epass-cutover-capability-gap.md`.** `apps/web` is not a
drop-in replacement for epass.io: it is a server-rendered redesign of epass.io's *browser*
surface, while epass.io is a JSON API with five clients hanging off it. The capability delta, the
per-client route delta, epass.io's own unmerged work, and what was and was not verified are all
in that doc.

~~**The cutover therefore waits on a scope decision nobody has taken:** whole replacement, or
browser surface only with `apps/api` left serving the native clients and the addon.~~
**Answered 2026-08-29 in the epass.io repo — browser surface only — and the whole question is
closed here as of 2026-08-31**
(`docs/decisions/2026-08-31-008-epass-cutover-closed.md`). The deploy blockers above are **not**
downstream of it: they are spec 011 T15, which is worth doing whether or not any other repo ever
deploys, and that is the only reason to read this section now.

## Perf re-measurement, 2026-08-24

The 2026-08-02 perf plan closed by asking for a re-measure before the epass.io cutover, on the
grounds that the box was noisy (±30% between identical runs). Done. **Both budgets pass with
room, and the k6 half of that plan is not this repo's to re-run.**

| Measure | Baseline 2026-08-01 | Now | Budget | Headroom |
| --- | --- | --- | --- | --- |
| Binary size | 36,918,272 B | 37,038,080 B (35.3 MB) | 42,500,000 B | 13% under |
| Time to `/healthz` | 881 ms | 537 / 544 / 550 / 593 ms | 5,000 ms | 89% under |

Binary grew 119,808 B (+0.32%) across two shipped specs and telemetry becoming opt-out, which is
noise rather than a trend. Startup roughly halved and, unlike the 2026-08-02 runs, was **stable**:
four samples inside a 56 ms spread, so the earlier ±30% caveat does not apply to this measurement
and no averaging is being hidden here.

The throughput numbers in `docs/plans/2026-08-02-002-perf-tail-work.md` (k6, 32 VUs, the
`pub_list`/`pub_tag` median-to-p95 gap) **cannot be re-taken here**: spec 003 extracted the
bookmarking app to manzil.my and `apps/` holds `web` alone, so the DB-backed list pages those
scenarios drove no longer exist in this repo. Same finding as the 2026-08-17 note at "Later" —
carry it to manzil.my's backlog rather than re-opening it as NativeWeb work. What remains here is
the publish-budget gate above, which `aot-publish` already runs on every CI build.
- ~~**The antiforgery-rejection guard exists twice and nothing enforces that they agree.**~~ —
  **done 2026-08-29**, `test/antiforgery-guard-agreement`. Built as this entry specified: one
  theory in `nativeweb.web.tests.unit` runs both through the three feature states (absent, valid,
  rejected) and asserts they agree, and the *test* project takes the scaffold ProjectReference so
  the production graph still says apps/web does not depend on CRUD scaffolding. Two things beyond
  the sketch: the scaffold copy is driven through a built `ApplicationBuilder` rather than a lifted
  lambda, so the registration is in scope; and each row asserts the expected outcome as well as
  agreement, because two copies drifting *together* would otherwise pass. Mutation-checked both
  ways — one guard to 403 fails, both to 403 also fails. The duplication itself is unchanged and
  still deliberate — and the standing rule survives it: **at a third copy, stop and give the guard
  one home** (shared-source `<Compile Include>` link, or a real library), because a test that two
  copies agree does not make three copies right.
- Both guards are a workaround for the RDG form binder not consulting
  `IAntiforgeryValidationFeature` before reading the form. Worth finding or filing the
  dotnet/aspnetcore issue and tagging both copies with its number, so they carry a deletion
  trigger rather than living forever. No issue number verified yet — do not cite one unchecked.

- **`Directory.Build.props`'s JauntyQ probes hard-code `extrode-legacy-org.com`.** Both fallbacks are
  `..\..\extrode-legacy-org.com\jauntyq` and `..\..\..\..\extrode-legacy-org.com\jauntyq` (lines 47-48), so the
  "more forgiving per-project build" is only more forgiving on a machine whose source root has
  that name; everywhere else the probes never match and `EnsureJauntyQRoot` names a path the
  reader has never had. `NativeWeb.slnx` by contrast uses the name-agnostic `../jauntyq/...`.
  Found by Fable's read-back on spec 010; the documentation was corrected to state the constraint
  rather than paper over it (`README.md` prerequisites, `docs/00-quick-start/getting-started.md`
  step 1). The
  fix in the build file — probe `..\..\jauntyq` and `..\..\..\..\jauntyq` too, before the named
  ones — was left out of a documentation branch deliberately.

  **Stale as of 2026-08-29 — this was fixed and the entry outlived it.** `Directory.Build.props`
  now probes `$(MSBuildThisFileDirectory)..\jauntyq` and `..\..\..\jauntyq`, both guarded by
  `Exists()`, and its own comment records that the named-ancestor form "broke twice in one move on
  2026-08-26". `JauntyQRootProbeTests` pins it, with a regex that refuses **any** org-named
  ancestor rather than just the old one — so writing `extrode.com` in would fail the same way.
  Nothing to do; kept as a record of the reasoning.
- ~~**A 15-minute publish budget assumes an idle machine.** All 13
  `nativeweb.scaffold.tests.e2e` facts failed at exactly `00:15:00` on 2026-08-25 with a NativeAOT
  publish that had not finished, while a `review-deep` worker was saturating the CPU. Re-run alone
  on the same commit: 13/13 green in 2 s. So the tier is not flaky in itself — it has no headroom
  under load, and a CI runner sharing a host would look exactly like this.~~ —
  **done 2026-08-30**, `fix/publish-budget-diagnostics`. **This entry named the wrong fixture:**
  `AppProcessFixture` (web e2e) had already been raised to 30 minutes; the 15-minute constant was
  `SampleProcessFixture` in the scaffold tier, which is what the 13 failing facts belong to. Raised
  to 30 to match, with the measurement recorded in the fixture's doc-comment so the next person to
  read it does not re-derive it. The second half of the entry — a timeout message naming the
  publish rather than surfacing as 13 assertion failures — was **not** done: the raise removes the
  trigger this machine has, and a message change would be a second, unmeasured guess at what the
  failure looks like when it does recur.
- ~~**A busy control can stick if the swap detaches the element that issued the request.**~~ —
  **done 2026-08-30**, `fix/busy-control-detached-source`. The symptom was real and now has a test.
  **The diagnosis in this entry was wrong in both halves**, and the proposed fix would have made it
  worse, so the correction is the part worth keeping.
  - It said htmx "does not re-target a detached node". It does: `trigger` ends
    `isConnected ? element : document`, so a detached source has its completion event re-dispatched
    on `document`, where the delegated listener hears it. `#normalizeElement` is a string-to-element
    lookup and has nothing to do with dispatch.
  - It said the fix was to bind the restore listener to the element with `{once: true}` instead of
    relying on bubbling. That **fails** the regression test rather than passing it — an at-target
    listener on the element htmx has routed around never fires. Tried, measured, reverted.
  - What breaks is that **htmx reassigns `ctx.sourceElement` to the swap target while swapping**.
    Instrumented on one submission: `before:request` gives `FORM#detached-source-form`,
    `finally:request` gives `DIV.auth-board`. `restoreBusy` read its reference back off that
    property, so it looked at an element that had never been given one. The reference now rides on
    the per-request `ctx` object, which htmx does not reassign.
  - Test: `BusyState_RestoredWhenTheSwapDetachesTheElementThatIssuedTheRequest`,
    in `ProgressiveEnhancementTests`, which builds the shape `apps/web` does not have — a submit attached from outside the swapped
    board with `form="..."`. **72 web e2e pass** (+1), 652 unit unchanged.
  - The general lesson, since it cost most of the time here: a backlog entry's *symptom* was
    evidence, its *mechanism* was a guess, and the guess read plausible enough to implement without
    checking. Instrumenting the page took one run and contradicted both readings.
- ~~**Back-navigation now re-issues GETs.** htmx 4 removed the `localStorage` page-history cache…
  nobody has walked the back button through the OAuth flow under 4.0 to check.~~ —
  **walked 2026-08-30**, `test/oauth-back-navigation`. Nothing to fix, and the premise was narrower
  than it read: **htmx's history layer is not involved in this flow at all.** Nothing in `apps/web`
  sets `hx-push-url`, `hx-boost` or `hx-history`, so every navigation in the OAuth flow is an
  ordinary browser navigation and back is the browser's own — under htmx 2 as much as under 4.
  What the walk does pin is the server's answer to a re-issued authorize GET after its code has
  been redeemed: a fresh consent screen, not a 500 and not a stale replay. The request counter is
  the assertion that carries it (**1 before approve, 2 after back**, mutation-checked: asserting 1
  fails with `Actual: 2`), because "re-fetched" inferred from a rendered page would also be true of
  a bfcache hit. **4 OAuth e2e facts pass** (+1).

- ~~**`docs/01-architecture/native-web-architecture.md:133` points at `docs/99-design/design-decisions.md`,
  which no tracked file matches.**~~ — **done 2026-08-29**, `docs/fix-dangling-design-reference`.
  The pointer is gone rather than repointed: nothing in the repo discusses this (`DbParameter` and
  `boxing` appear in exactly one file, that one), so the note was never written rather than moved
  in the spec 010 restructure. The sentence already carried the whole argument; it now finishes it
  — once per call, not once per row, which is why the cost does not scale with result size.

  **Do not build a repo-wide broken-path test for this.** Measured with a throwaway probe over all
  114 tracked `.md` files: **88 markdown links, 0 broken**, so the link form is already clean. The
  prose form is 664 backticked repo-path mentions with **198 unresolved**, and nearly all of those
  are false positives the probe cannot tell apart — paths relative to a project directory
  (`Auth/SessionAuth.cs`, `locales/en.json` under `apps/web`), upstream vendor paths (`dist/htmx.min.js`
  in the htmx repo), sibling-repo paths (`JauntyQ.Generator/AutoCrud.cs`), illustrative examples
  (`templates/layout/page.html`, which exists in `skeleton/`), and `../`-relative links the probe
  resolved as repo-rooted. Filtering to live documentation left **14**, of which exactly **one**
  was this defect. A test at that signal-to-noise would be turned off within a week.

- ~~**`tools/bench/run.ts` publishes into the same tree the e2e fixture uses.**~~ —
  **done 2026-08-29**, `fix/bench-publish-path`. `publish()` now pins `-o tmp/bench/publish` and
  `findPublishedExe` looks only there. `tmp/` rather than the repo-root `publish/` that
  `check-publish-budgets.ps1` uses, because that one is deliberately **not** gitignored (justsmtp
  commits a top-level `publish/`), and a throwaway NativeAOT publish where `git add -A` can see it
  has cost this repo a bad commit before. The RID scan is gone with the collision: pinned output
  means one directory, so "several RIDs, take the newest" no longer has a question to answer. Three
  new harness tests, one of them the regression itself — an e2e binary sitting in
  `apps/web/bin/Release/net10.0/win-x64/publish` must now be **refused**, not benchmarked. 97 pass;
  mutating `PUBLISH_DIR` back to the e2e path fails two of them. **The committed
  `baseline.json` is unaffected** — it records throughput and RSS, not paths, and the binary is
  the same publish either way. `scripts/cleanup/012-bench-harness.ps1` updated: the harness's own
  output is now in scope, the e2e fixture's still is not. The original entry:
  `publish()` runs
  `dotnet publish apps/web -c Release` with the default output path, which is the
  `apps/web/bin/Release/net10.0/*/publish` directory `tests/nativeweb.web.tests.e2e/AppProcessFixture.cs`
  also publishes into and launches from. A bench run interleaved with the e2e tier has the two
  writing one directory. `scripts/ci/check-publish-budgets.ps1` avoids this by pinning `-o publish/web`.
  Not fixed on the 012 harness branch: pinning an output path also changes what `--skip-publish`
  reuses and what `findPublishedExe` scans, and the committed baseline was recorded against the
  current layout. Found by the consequence-lens review of `feat/012-bench-harness`.

## ISL family 1.1 and ISL-CLA 1.1 — notice from the family maintainer (2026-08-30)

From `ikhbat.org/islamiclicense.org`, `CHANGELOG.md` under `## [Unreleased]`, pushed as `ad5291a`.
The re-syncs are yours; none of this is urgent. Ordered by what it costs to leave alone.

- **1.1 retires the `NOTICE.md` "Who the Licensor is" workaround.** ISL-NETC 1.1 §1.2 reads
  `[LICENSOR]`, and its *Notice on Reproducing This License* now says in terms that filling the
  bracketed fields "is not a modification of this text for the purposes of this notice. A copy so
  completed remains an unmodified reproduction of this License." So §1.2 can carry "Extrode LLC"
  in `LICENSE.md` itself, and the section that currently reconciles a Foundation-named §1.2 with a
  third-party licensor stops being needed. Optional — a verbatim 1.0 copy with that documented
  workaround stays internally coherent — but this is the whole reason 1.1 exists.

- **`NOTICE.md:13` cites a drifting URL.** `https://islamiclicense.org/isl-netc/` always serves the
  latest text: it served the 1.0 you ship when the line was written and serves 1.1 now, so the
  notice and `LICENSE.md` no longer name the same document. Every version has a permanent URL as of
  spec 003 — `https://islamiclicense.org/isl-netc/1.0/LICENSE.md` for what you ship today,
  `/isl-netc/1.1/LICENSE.md` if you re-sync. `work/todo.md:62` above records that the pinned form
  404s; it does not. The frozen 1.0 texts are published and `dev` is pushed.

- **The fourteen frozen 1.0 texts each gained one blank line** in commit `5b9351b`, restoring the
  blank line above a `### 4.11` heading. Whitespace only: no word, clause, section number or
  obligation differs. A byte comparison of your `LICENSE.md` against the published 1.0 fails on
  that alone, and that is the entire delta.

- **ISL-CLA 1.0 is frozen at four adoption items; 1.1 requires five.** `CONTRIBUTING.md:14` already
  adopts 1.1 by reference at `https://islamiclicense.org/isl-cla/1.1/CLA.md`, which is live. Both
  versions remain **DRAFT — not yet in force** pending counsel review; adoption by reference is
  sound today, but nothing is in force until that banner comes off.

- **ISL-CLA Section 2 does not reach ISL-EULA** — no action here, recorded because the same notice
  went to four repos. §2 supplies the prior written consent only where the licence grants no right
  to modify or adapt: ISL-R, ISL-CW-ND and ISL-CW-NC-ND. ISL-NETC permits modification, so §2 never
  applied to NativeWeb and your `CONTRIBUTING.md` does not rely on it.

**Answered 2026-08-30 on `docs/isl-netc-1.1`.** Bullets one and two are done: `LICENSE.md` is the
1.1 text with §1.2 completed as Extrode LLC, the `NOTICE.md` override section is gone, and both
`NOTICE.md:13` and `README.md` now cite the version-pinned `/isl-netc/1.1/LICENSE.md`. Bullet three
is moot rather than fixed — the one-blank-line delta was against the frozen 1.0 text, which this
repository no longer carries. Bullets four and five need nothing: `CONTRIBUTING.md` already adopts
ISL-CLA 1.1 by reference with five statements, and its **DRAFT — not yet in force** status is
unchanged by any of this, so the banner question stays open and stays theirs.

## ~~Deploy toolchain hardcodes SSH port 22~~ — FIXED 2026-08-30 (`fix/nft-ssh-port`)

`scripts/deploy/nftables/nativeweb.nft:43-44` accepts `tcp dport 22` only, and `provision.sh:351`
installs it as `/etc/nftables.conf` and enables it. On a host with sshd on a non-default port and 22 closed at
the provider firewall and not bound by sshd — that is an immediate lockout with no console failover, in
the middle of the provisioning step. Fix is to derive the ports from `sshd -T` and emit an
nftables `define` rather than hardcoding, so the ruleset opens whatever sshd actually listens on.
Blocks spec 011 T15 at its first step.

## Deploy target moved to an Incus host — three follow-ups

`nativeweb-dev`
(`10.0.0.30`) and `nativeweb-prod` (`10.0.0.31`) exist, are hardened and are idempotent;
a separate build host is the NativeAOT builder. Three T15 stoppers from the earlier VM rehearsal are
gone with the host change. What is left is code, not infrastructure:

- **Caddy needs a behind-an-edge variant.** The host terminates TLS at one host Caddy and speaks
  plain HTTP to a private address, so the VM's Caddy moves from `:443`-with-ACME to `:80` with
  `auto_https off`. Keeping that sidecar rather than dropping it is what preserves R10's zero-gap
  blue/green swap, since `deploy.sh` promotes by rewriting `upstream.caddy` and reloading. Needs
  the Caddyfile variant, the matching `deploy.sh` branch, and tests for both.
- **Guest firewall rule.** The golden image's default-deny `inet filter` needs
  `ip saddr 10.0.0.1 tcp dport 80 accept` — the bridge gateway only, per the host
  convention. Waiting on the sidecar port above.
- ~~**`deploy.sh` still has no SSH port flag**~~ — **done 2026-08-31** (`fix/014-leftovers`).
  `--port` / `-p`, validated before anything is copied, reaching both the `ssh` calls as `-p` and
  both `scp` calls as `-P` from one pair of arrays — the two spellings differ, and a deploy where
  they disagree ships the artifact to one host and re-executes on another. Refused outright with
  `--incus`, which opens no connection a port could apply to. Omitted, `ssh_config`'s `Port` for
  the host still applies, so a `Host` alias that already carries one needs nothing. Five facts
  (three rejected spellings, the `--incus` refusal, and a structural one that no bare `ssh "$HOST"`
  or `scp ... "$HOST"` is left behind); the mutation that makes `scp` take `-p` kills it.
  Adding three lines to the header comment also silently truncated `--help`, which
  `AssertHelpRangeIsIntact` caught — the `sed -n '2,25p'` range is now `2,28p`.

- **Both Caddyfiles trip `caddy fmt`** on the blank line between the header comment and the
  global options block (`caddy validate` still passes; it is a formatting warning, not an
  error). Left alone rather than fixed in one file, which would make the pair inconsistent.

- ~~**`DenoToolingPolicyTests.Repo_HasNoNodePackageManifestOrLockfile` failed once in a full-suite
  run on 2026-08-30**~~ — **fixed 2026-08-31** (`fix/014-leftovers`). Two causes, both in the walk
  rather than the rule: `Directory.Enumerate*` is lazy and throws `DirectoryNotFoundException`
  when a directory is removed mid-iteration, and `tmp/` — where every other test in the tier puts
  its scratch — was being walked at all. `tmp` joins the skip list, and each directory's children
  are taken as a snapshot inside a guard. Two facts, both killed by the matching mutation.

- **`nativeweb-prod` has no sidecar firewall rule and no provisioning.** `nativeweb-dev` has
  both. Deliberate: prod waits until dev has served a real release.

## Spec 014 leftovers — noted during the pre-merge review, out of that branch's scope

- ~~**The `NWF010` analyzer has three blind spots**~~ — **the false positive is closed
  2026-08-31** (`fix/014-leftovers`). The analyzer now checks that the receiver is or implements
  `Microsoft.AspNetCore.Routing.IEndpointRouteBuilder`, so a user-defined `MapPost` on somebody
  else's builder is left alone. Two facts pin it and a third pins the instance receiver, since
  `WebApplication` implements the interface itself. **Measured while doing it:** IOperation hands
  an analyzer the UNREDUCED symbol for both call spellings — `app.MapPost(...)` and
  `EndpointRouteBuilderExtensions.MapPost(app, ...)` both arrive with a null `Instance`, a null
  `ReducedFrom`, and the `this` as `Parameters[0]`. A first cut read `ReducedFrom` first and the
  branch never ran. The test stub also had `IEndpointRouteBuilder` in
  `Microsoft.AspNetCore.Builder`, one namespace off from the real one, so it would have passed a
  test the framework fails.

  **Two blind spots remain and are still open**, both a route the analyzer cannot see rather than
  one it misreads: a handler delegate held in a local or a field and passed by name, and a plain
  route whose handler calls `MyForm.BindFormAsync(ctx)` by hand. Neither is a false positive —
  both are a real defect going unreported, which is the safer direction to be wrong in.

- **`PublicArticleEndpoints.Matching` filters within the already-taken page**, not the feed, so
  `?q=` searches the first N articles rather than all of them. Right for the R5 publish gate it
  exists to serve; wrong as an example anyone copies.

- ~~**`DeclarativeFormTests` asserts exact article counts**~~ — **done 2026-08-31**. Both facts
  now measure against the unfiltered feed rather than a literal, so seeding another article no
  longer fails a test it has nothing to do with. `A_renamed_field_binds_under_its_wire_name` also
  asserts the unfiltered feed is longer than the cap, because a cap of 1 against a feed of 1
  proves nothing. Two mutations, each killing both facts.

- **The runtime packages do not reference their generators — all three families, not just
  Forms.** `NativeWeb.Forms`, `NativeWeb.Scaffold` and `NativeWeb.Render` each ship the attributes
  and the runtime contract with no path to the generator that reads them, so `[Form]`, `[Scaffold]`
  and the render attributes would do nothing for a consumer installing the runtime package, with
  no diagnostic saying why. **Left for a decision rather than fixed**, because there are two
  defensible shapes — the runtime package taking a `PackageReference` on the
  `DevelopmentDependency` generator package, or the generator DLL being bundled into the runtime
  package's `analyzers/dotnet/cs` and the split PackageIds dropped — and picking one is picking a
  publishing model, which is the open question this list already carries. No consumer exists
  today: nothing installs these from a feed.

- ~~**`FormModel.FullyQualifiedName` is dead code**~~ — already gone; the property does not exist.
  The unused `using System.Collections.Generic;` in `MissingAntiforgeryAnalyzer.cs` is **removed
  2026-08-31**.

- ~~**`DeployScriptTests.cs:727` points the wrong way**~~ — **fixed 2026-08-31**; the reference
  reads "above", which is where `The_ssh_allowance_is_not_hardcoded` is.

- `NativeWeb:BotTraps:MinAgeSeconds` parses with `NumberStyles.Float`, so `1e9` binds and
  disables every form submission for ~31 years. Pre-existing (the skeleton's `Program.cs` parsed
  the same way); found while writing spec 015 T5's binder tests. An upper clamp, the way
  `Readiness:CacheSeconds` has one, is the fix.

- ~~**Scaffold: `?page=2000000000` overflows to a negative offset.**~~ Fixed in T11, 2026-08-31:
  `Math.Clamp(…, 1, int.MaxValue / PageSize)`. The whole T11 batch went with it — index not gated,
  short/float wouldn't compile, `[ScaffoldSearch]` never reached the store, silent coercion,
  `DeleteAsync` ignoring false, the update race.

- ~~**Scaffold: a created record's key is `default!`.**~~ Addressed in T11, 2026-08-31: the contract
  on `IScaffoldStore.CreateAsync` now states that the store assigns a unique key, and the generated
  create redirects to the key it returned rather than discarding it — so a store that hands back a
  stale or empty key fails on the next request instead of at the next silent overwrite.

- **`NativeWeb.Web.targets` is packed to `build\` only, which is not transitive.** A product
  reaching the package through an intermediate gets the assemblies and none of the wwwroot assets,
  failing as runtime 404s rather than a build error. `buildTransitive\` alongside `build\` is the
  fix. Unexercised today: nothing installs these from a feed (see the publishing-model question
  above).

- ~~**`ForwardedHeadersOptions.ForwardLimit` is left at 1 while `KnownProxies` takes a list.**~~ —
  **done 2026-09-02**. The decision went to the user, who took three independent opinions (fable,
  glm-5.3, qwen3.8-max) first; all three picked binding it over deleting the seeded line, and all
  three agreed the trust check gates *which* hops unwind while the limit caps *how many*, so
  raising it is not "one more spoofable hop" — the walk still stops at the first address that is
  not a known proxy. `Caddyfile.edge` was checked as well: it trusts the edge and appends, so the
  two-hop chain the comment describes is the one that arrives. `NativeWebOptions.ForwardLimit`
  binds the key and fails startup on anything but a positive whole number,
  `AddNativeWebForwardedHeaders` applies it, `configuration.md` carries a real row where the "Not
  read, despite appearances" section was, and `DocumentedAsUnread` is empty. Original entry:

  Two
  trusted hops (CDN in front of a reverse proxy) means only the last XFF entry is consumed and every
  client shares one rate-limit partition. Raising it trades that for one more spoofable hop, so it
  is a decision, not a fix. Found in the 015 review, 2026-08-31.

  **And the provisioner already thinks it made that decision.** `scripts/deploy/provision.sh:182`
  writes `${CONFIG_PREFIX}__ForwardedHeaders__ForwardLimit=2` under `--edge`, with a comment
  explaining the two-hop case it is for. Nothing binds it: `AddNativeWebForwardedHeaders` sets
  `ForwardedHeaders` and `KnownProxies` and never touches `ForwardLimit`, so an edge deployment
  runs at the default of 1 and sees every request as coming from the proxy — the failure the
  comment says it prevents. Found while writing spec 018; out of that spec's scope, so the key is
  documented as unread in `configuration.md` and held there by `DocsClaimsTests`. The fix is
  either to bind it or to stop writing it, and which one is the decision above.

- ~~**The departed `NativeWeb:` prefix survives in source comments and in two user-visible error
  messages.**~~ — **done 2026-09-02**, `dc4d3d9`. Both messages are section-relative now
  (`Smtp:FromAddress`, `Smtp:SecurityMode=None`), the four `ContactProtector` comments name no key
  at all, and `DepartedPrefixTests` sweeps every `.cs` and `.md` under `libs/` so the next copy
  fails rather than reaching a README. Original entry:
  `libs/nativeweb.mail/SmtpOptions.cs:23` says `SmtpOptions` binds from
  `NativeWeb:Smtp` and lines 103 and 113 throw `InvalidOperationException` naming
  `NativeWeb:Smtp:FromAddress` and `NativeWeb:Smtp:SecurityMode`; `ContactProtector.cs` names
  `NativeWeb:ContactKey`. Neither library binds anything — the host chooses the section — and that
  prefix left with `apps/web` on 2026-08-31. It was copied straight into a draft README before
  being caught, which is the reason to fix it rather than leave it: a stale comment is a source
  documentation is written from. Found writing spec 018, 2026-09-02.

- **`MaxRequestBodyBytes` and `RequestTimeout` have no configuration key.** Framework-wide ceilings
  a product cannot tune without forking `NativeWebOptions`. Same review.

- **`UseForwardedHeaders` sits above `ErrorPageMiddleware`.** A throw from it escapes to Kestrel as
  a bare 500 rather than the error shell. The order is deliberate and tested (the rate limiter needs
  the real IP), so moving the shell above it is a change to a recorded decision, not a bug fix.

- **Scaffold: `ScaffoldValues.Decimal` accepts a group separator and `Double`/`Single` do not.**
  `Decimal` parses with `NumberStyles.Number`, so "1,5" is 1500 to it; the float converters use
  `NumberStyles.Float` and reject the same string. Each validator matches its own converter, so
  nothing is inconsistent within a type — but two number fields on one form disagree about what a
  comma means. Narrowing `Decimal` would reject input that has been accepted and stored, so it is a
  decision, not a fix. Found writing the T11 validator tests, 2026-08-31.

- **Scaffold: a blank non-nullable numeric still takes the converter's fallback.** T11 made
  *malformed* input an error; an omitted `int` field is still 0 and an omitted non-nullable
  `DateTime` is still `DateTime.UtcNow`. Making blank "required" the way a non-nullable string is
  would be the consistent rule, and it is a behaviour change for every existing form. Same batch.

- **Scaffold: a key equal to a literal route segment collides with the route.** A store that
  returns `new` as a row key gives that row the URL `/admin/articles/new`, which is the create
  form's route and wins by registration order — the row becomes unreachable and unentry-editable.
  A framework fix is either a reserved-key check in the generated create, or moving the create form
  off a path segment (`/admin/articles?new`). Found in the T11 review, 2026-08-31.

- **Scaffold: `JauntyArticleStore` accepts `searchFields` and ignores it.** T11 made
  `[ScaffoldSearch]` reach the store, and the memory store honours it; the SQL path still has the
  columns fixed in the `db/sample/queries/Articles/ListBy*.sql` set, so removing the attribute
  changes the placeholder and not the query. Honouring it properly needs either generated SQL or a JauntyQ
  query that takes a column list. Same review.

- **Scaffold: labels are C#-escaped but not HTML-encoded into the emitted markup.** `Escape` makes
  a label safe as a C# string literal; a `Display` or `ScaffoldList(Label = ...)` carrying `<` or a
  quote lands in the generated HTML unencoded. The values come from a developer's own attributes,
  not from a request, so the reach is a broken page rather than injection — but the encoder exists
  and the emitter should use it. Same review.

- **Scaffold: a repeated delete answers 404 rather than the list.** T11 made `DeleteAsync`'s false
  a 404, which is right for a row that was never there; a back button or a double click on a row
  that *was* deleted now shows an error page where a redirect to the list would read as success.
  Distinguishing the two needs the store to say which happened. Same review.

- ~~**`scripts/deploy/` preflight and `provision.sh` guard the wrong configuration section.**~~
  **Fixed 2026-08-31** on `fix/021-review-findings`. `build.sh` stamps `config_prefix` and
  `migrations_dir` into the artifact's RELEASE file, `deploy.sh` reads both and builds every key
  from the prefix, `provision.sh` takes `--prefix`, and the ten dead keys are gone. Two facts in
  the entry below were understated and are recorded here because the wording would have sent
  someone debugging the wrong mechanism: the key ring does not "fall back to the app directory
  and move on a swap" — the app directory is *unwritable* under `ProtectSystem=strict` with
  `ReadWritePaths=/var/lib/nativeweb`, so it never persists at all; and the telemetry gate was
  inverted, asserting an opt-out posture the binder cannot produce. `NativeWebOptions` now
  refuses to start rather than composing a relative key path. The original entry, for the record:
  `preflight_config` requires `ConnectionStrings__NativeWeb`, `NativeWeb__WebAuthn__*`,
  `NativeWeb__DeviceGrant__VerificationUri`, `NativeWeb__ContactKey`, `NativeWeb__StoreEncryptionKey`,
  `NativeWeb__PairwiseSalt` and `NativeWeb__Smtp__*` — the departed IdP's keys — while `apps/sample`
  binds `Sample:` and takes `ConnectionStrings:Sample`. Two effects, both from the mismatch the OTel
  gate had: a deploy is blocked until an operator fills keys the binary never reads, and the keys it
  *does* read go unchecked and unseeded. `provision.sh:180`'s
  `NativeWeb__DataProtection__KeyDirectory=/var/lib/nativeweb/keys` is the sharp one — unread under
  the `Sample` prefix, the key ring falls back to the app directory and a blue/green swap moves it
  out from under every issued antiforgery token, which is the thing `nativeweb@.service:110-114`
  says must not happen. The fix is a `-ConfigPrefix` equivalent for the deploy scripts plus a prune
  of the IdP keys: bigger than T12's retarget, and it wants a decision about what a stamped
  product's prefix is. Found in the T12 review, 2026-08-31; no host runs these scripts (T15), so
  nothing is affected today.
- **`deploy.sh` re-grants world read+execute on the launcher.** `chmod -R g-w,o-rwx "$RELEASE_DIR"`
  followed by `chmod 0755 "$RELEASE_DIR/nativeweb"` leaves one hole in the release tree's permission
  intent. Pre-existing, unchanged by T12. Same review.
- **Nothing re-checks the live colour's launcher name.** A release staged before 2026-08-31 holds
  `nativeweb.web`, which now fails the unit's `ConditionPathExists` and is skipped *silently* on the
  next reboot or restart; `deploy.sh` checks only the colour it is staging, and `rollback.sh` notices
  only after the previous colour has gone inactive. Unreachable today because no host has ever run
  these scripts. Same review.

## ~~Coverage gaps named during the 021 review, 2026-08-31~~ — all four closed, 2026-08-31

Built on `test/021-coverage-gaps`. Kept rather than deleted because two of the four came back with
an answer different from the one the audit assumed.

- ~~**No LanguageVersion gate on the generator's output.**~~ Built, and it found a fact rather than
  a defect: the emitted source needs **C# 11**, not the C# 10 the audit proposed as the floor.
  `ScaffoldRuntime.g.cs` emits a raw string literal (CS8936), and the form binder implements
  `IBindableFromHttpContext<T>`, whose `BindAsync` is a static abstract interface member (CS8706).
  The second is not ours to lower, so a consumer below C# 11 could not bind a minimal-API parameter
  at all. The theory pins 11, 12 and 13, and the parse options now travel to the generator driver
  as well as the model tree.
- ~~**The update-false → 404 path is asserted from the emitted source, never executed.**~~ Built:
  `ScaffoldedConcurrencyTests` registers a stub store through `ConfigureTestServices` whose
  `GetAsync` returns the row and whose `UpdateAsync` returns false, which is the concurrent delete
  made deterministic.
- ~~**`TryResolveKeyDirectory`'s empty-home branch has no behavioural partner.**~~ Built as
  `SampleStartupRefusalTests`, but **not on that branch**: an unusable home cannot be produced from
  the environment on Windows, because `GetFolderPath(SpecialFolder.UserProfile)` reads the process
  token rather than `USERPROFILE`. The boundary fact every refusal shares — a line on stderr, exit
  78, and no modal block — is proved with a malformed `BotTraps` and `Telemetry` value instead.
- ~~**Nothing pins the `[ScaffoldSearch]` wiring.**~~ **Already covered; the entry was wrong.**
  Emitting an empty `SearchFields` set turns `ScaffoldedCrudTests.Search_FiltersOnTheSearchableProperties`
  and `ScaffoldedDefectTests.Search_MatchesEverySearchableProperty` red. Measured by mutation
  2026-08-31 rather than assumed.

## Found while closing those gaps, 2026-08-31 — not fixed

- ~~**`StaticErrorShell.Load` throws out of `Main` instead of calling `FailStartup`.**~~
  **Fixed 2026-08-31, `fix/static-error-shell-exit`.** Reached by starting `apps/sample` with a
  working directory that has no `wwwroot`: the host died with
  `InvalidOperationException: wwwroot/pages/error.html is missing` and exit code `0xE0434352`.
  That is the class of failure `lessons/2026-07-26` and `StartupConfigGuards` exist to prevent —
  under NativeAOT on Windows it is a modal box a supervisor sees as a hung service, not an error.
  The `IWebHostEnvironment` overload — the one on the path out of `Main` — now catches
  `InvalidOperationException` and `IOException` and routes them to `FailStartup`, so the shell
  leaves by the same stderr message and exit 78 as a refused configuration value. The
  `IFileProvider` overload still throws: it is the pure function the unit tier drives, and a test
  cannot observe `Environment.Exit`.
- **Six untracked test directories are `bin`/`obj` residue from the departed identity provider**:
  `tests/nativeweb.signin.epass.tests.{unit,integration,e2e,aot}`, `tests/nativeweb.web.tests.integration`
  and `tests/nativeweb.web.tests.e2e`. No `.csproj`, nothing in `NativeWeb.slnx`, nothing in git.
  Listed in `scripts/cleanup/021-coverage-gaps.ps1`; the user runs it.

- **D2 is answered and the generated admin stays open by default (2026-08-31, the user's call).**
  Recorded here because the argument for closing it does not go away: `AddScaffolding` refuses to
  register an authentication scheme on the grounds that "a default supplied from a library is one
  nobody reviews", and a posture supplied by omission is equally unreviewed. If a stamped product
  ever ships an unguarded model by accident, this is the entry to reopen.

## Found during the first real deploy, 2026-09-01 — not fixed

- **`docs/02-reference/configuration.md` documents an app that left the repository.** Its title
  is "Configuration reference — apps/web", every key it lists is `NativeWeb:`-prefixed, and the
  sections it is built out of — OAuth, WebAuthn, Mail, Breach checking, Passphrase reset,
  `/oauth/introspect`, `bff_store` — went with the identity provider on 2026-08-31.
  `apps/sample` binds `Sample:`. 302 lines. **Rewritten 2026-09-02 by spec 018** against the keys
  the binders read, and held there by `DocsClaimsTests`, which extracts them from the sources on
  every run. One claim above was half wrong and is corrected here: `CanonicalBaseUrl` is read
  nowhere, but **`SecurityContact` is read**, at `libs/nativeweb.web/NativeWebPipeline.cs:545`,
  where it feeds `/security.txt`.
- **`/robots.txt` returns 404 on the deployed app, and that is right.** Nothing serves it — the
  endpoint left with `apps/web`. The 404 only looked like a defect because
  `/etc/nativeweb/app.env` on `nativeweb-dev`, written by a provisioning run predating the
  removal, still calls `CanonicalBaseUrl` "REQUIRED, emitted by robots.txt and sitemap.xml".
  Whether a framework should ship `robots.txt`, `sitemap.xml` and `security.txt` as generic
  primitives is a real question and the answer is not obviously no; it belongs in a spec, not
  in a doc fix.
- **`nativeweb-dev` carries dead keys from that same run.** `app.env` still sets
  `Sample__WebAuthn__ServerDomain`, `Sample__WebAuthn__Origins` and
  `Sample__DeviceGrant__VerificationUri`; `secrets.env` carries five dead SMTP and IdP keys.
  Nothing reads any of them. Host state, not repo state — an operator's edit.

## Found by the 017 code review, 2026-09-01 — not fixed, out of scope

- **Keyset paging by a nullable column keeps its position with a Filter, not an Index Cond.**
  Measured with `EXPLAIN` against `nativeweb_sample_test`: `ListByAuthorAsc.sql` gets an
  `Index Scan using articles_author_id_idx` with no sort node, but the boundary
  `author IS NULL OR (author, id) > (...)` lands in `Filter`. A disjunction over `IS NULL` is not
  a rangeable start point, so page N walks the index from the top. The order is free; the
  position is O(rows before it). Fixing it means folding null placement into the index as an
  expression — `((author IS NULL), author, id)` — and rewriting both author queries against it;
  a plain row-value comparison cannot express it, because NULL in any component yields NULL.
  Non-null sort columns are unaffected. Noted in `0002_scaffold_sort_indexes.sql`.
- **A cursor is bound to the sort property and direction, not to `?q=`.** Carrying an `?after=`
  across a changed search term applies a position from one result set to another. The search form
  drops `after` on submit, so only a hand-edited URL reaches it, and the outcome is a wrong window
  rather than an error. Binding the search term into the cursor would close it.
- **`CountAsync` runs on every list request, on its own connection, against the unindexed
  three-column `ILIKE`.** Pre-existing — the offset pager needed it too — but keyset made the page
  itself an index range scan, so the count is now the only unbounded work in the request. Its
  snapshot is also separate from the page's, so "N items" can disagree with the rows above it.
- **The in-memory store's ordinal text comparison is untestable in this repo.**
  `Directory.Build.props` sets `InvariantGlobalization=true`, so `string.CompareTo` is already
  ordinal in every process here and a test asserting the difference passes with the fix reverted.
  The fix stays — `IComparable.CompareTo` on a boxed string is `CurrentCulture`, and a consumer
  that does not set that flag gets an order that changes with the host's locale — but the
  assertion was removed rather than kept as one that cannot fail. Proving it needs a test host
  built without the property.

## Found by the 018 mutation triage, 2026-09-01 — recorded, not chased

- **Four equivalent mutants survive on purpose.** No test can kill a change with no observable
  difference, and Stryker has no equivalent-mutant detection, so they are named here rather than
  left looking like gaps. `ScaffoldSort.cs:157` — `MaxEncodedChars` is 2048, but the decode buffer
  caps a payload at 1,160 bytes, so no token above ~1,548 chars can decode and `>` never differs
  from `>=`. The constant is a redundant guard, not a wrong one. `ScaffoldValues.cs:114` —
  `AdjustToUniversal` does not change whether `TryParse` succeeds, and `IsDateTime` returns only
  that. `ScaffoldValues.cs:177` — a `break` whose loop would reassign the same value.
  `ScaffoldAccess.cs:71` — `ConfigureAwait(false)` flipped to `true`.
- ~~**The `sample-integration` triage target leaves live rows behind, and they are not inert.**~~
  — **done 2026-09-02**, spec 019: both fixes, not one. `tools/stryker-config.json` raises the
  per-mutant additional timeout to 30 s so fewer hosts are killed, and both tiers' feed tests
  sweep feed-prefixed future-dated rows minted more than thirty minutes earlier before they
  insert, age-gated because Stryker's parallel hosts share the database. Original entry:
  Stryker kills a host it judges hung, so the killed process skips the `finally` that removes its
  fixture rows, and the feed suites date theirs five years out on purpose. One run left five
  future-dated published rows in `nativeweb_sample_test` and broke an e2e feed test an hour
  later. Documented in `tools/mutation-triage.ts`'s header and listed by
  `scripts/cleanup/018-paging-property-tests.ps1`; the durable fix would be a Stryker
  `--dev-mode`/higher timeout for that target, or fixture rows that a session-scoped sweep can
  find by prefix without a `finally`. Neither is done.
- ~~**`ConfigureAwait` mutants inflate every survivor count in this repo.**~~ — **done
  2026-09-02**, spec 019: `tools/stryker-config.json` with `ignore-methods: ["ConfigureAwait"]`,
  passed by the triage tool, and the baseline re-recorded under it. Original entry: Roughly fifteen of
  `JauntyArticleStore`'s thirty-four integration-tier survivors are `.ConfigureAwait(false)` →
  `true`, which is behaviour-identical under a test host. A `stryker-config.json` with
  `ignore-methods: ["ConfigureAwait"]` would make the raw numbers mean what they look like.
- **The generated list's paging arithmetic has no unit-reachable composition point.** The
  request-`PageSize + 1`, trim-the-tail-when-backward and build-three-tokens logic is emitted
  inline by `ScaffoldEmitter`, so the store↔endpoint interaction — where all three high 017
  defects lived — is only testable through HTTP. Extracting it into a `ScaffoldPaging.From(...)`
  helper in `libs/nativeweb.scaffold` would make it property-testable at the unit tier and
  shrink every consumer's generated output. A production refactor of the generator, so out of
  scope for a test branch.
- **`DemoSignIn.cs` scores 17.2% and `Models.cs` 0.0%.** Neither was in this branch's scope. The
  triage figures are in `tools/mutation-baseline.json` if either becomes worth closing.
- ~~**`Encode` substitutes for an unpaired surrogate; `TryDecode` no longer does.**~~ — **done
  2026-09-02**, `aa15806`. `Write` became `TryWrite` over `Utf8.FromUtf16` with
  `replaceInvalidSequences: false`, and `Encode` returns the empty string when it fails, which is
  the refusal path an over-long part already took. A paired surrogate still round-trips. Original
  entry: After the strict
  UTF-8 read, a token `TryDecode` accepts re-encodes to itself — but `Encode` still calls
  `Encoding.UTF8.GetBytes`, which replaces a lone surrogate in `Property`, `SortValue` or `Key`
  with U+FFFD. A row whose sorted column holds one therefore yields a cursor that decodes to a
  *different* cursor, and the reader lands on the wrong window rather than an error. Pre-existing,
  not reachable from `apps/sample`, and the property tests do not generate surrogates. Closing it
  means a strict `Utf8.FromUtf16` in `Encode` returning the empty string on failure, which is the
  refusal path that already exists for an over-long part.

- **`ClientIpKey` folds every request without a remote address into one `unknown` partition.**
  Pre-existing in the general policy; spec 020's sign-in limit of 10 a minute now shares it, so ten
  such requests lock the only route into a deployed admin for all of them at once. Live only if the
  `KnownProxies` preflight fails to hold. Found in the 020 review, 2026-09-02.
- **The e2e https fixtures leave `tmp/scaffold-e2e/kestrel.pfx` on disk at a fixed path.**
  Gitignored, but a second https fixture would race on it and nothing removes it. Pre-existing in
  the sign-in tier; copied by the 020 fixture. Found in the 020 review, 2026-09-02.
- **`ProductionWithoutTheSecretExitsNamingTheKey` pins no framework key.** If a framework
  Production guard ever fires before the passphrase guard on a runner (a data-protection home that
  does not exist, say), the exit is still 78 but names another key. Found in the 020 review,
  2026-09-02.
- **`RequestBase.PublicBase` builds `https:///` when the Host header is empty**, and nothing
  filters the host: a feed or sitemap fetched with a forged `Host` links to that host, and the
  output cache keys on it. Fine behind Caddy, which only forwards its own site; a product exposed
  directly should add host filtering. Found in the 021 review, 2026-09-02.
- **`FeedWriter.Resolve` throws `UriFormatException` on a malformed link** rather than refusing
  with an `ArgumentException` naming the entry. Found in the 021 review, 2026-09-02.
- **`PublicPageCache.CarriesIdentityCookie` is a fixed list a product cannot extend.** The sample's
  `nw-sample` and demo session cookies are covered because the framework knows them; a product
  with its own session cookie name would be cached while signed in. Found in the 021 review,
  2026-09-02.
- **`/articles.html` is a second URL for the listing and nothing tests or canonicalises it.**
  Found in the 021 review, 2026-09-02.
- **The CSP header still carries a nonce on non-HTML responses.** Harmless, since no script runs
  in JSON, and R8 now says "depend on no nonce" rather than "no nonce"; stripping it would be a
  change to `SecurityHeadersMiddleware`. Found in the 021 review, 2026-09-02.
- **`AssetParityTests`' sweep excluded `tmp/` but not `.worktrees/`**, so any worktree turned the
  repo suite red. Excluded on the 021 branch; the sweep still walks the whole tree, so a slow disk
  pays for it. Found 2026-09-02.

## Found by the 023 code review, 2026-09-03 — noted, not fixed

- **Two admins saving the same link from opposite sides can deadlock on the SQL path** (40P01):
  the article save locks its `articles` row then rewrites `article_tags`, the tag save locks its
  `tags` row then rewrites the same links, and the cycle closes when the article save also
  changes `slug`, a key column the link's FK holds `FOR KEY SHARE`. Reproduced with two psql
  sessions on the rehearsal database; no catch filter names 40P01, so the loser is a 500 rather
  than a re-rendered form. Catching it and refusing with "saved by someone else, try again" is
  the fix; it needs a deterministic test first. Found in the 023 review.
- **The memory category delete cascade runs under the category store's lock, not the article
  store's**, so an article save that passed the category check can land pointing at a deleted
  category; the SQL path's `ON DELETE SET NULL` cannot. The `MemoryStore.OnDelete` remark claims
  otherwise. Demo store only. Found in the 023 review.
- **A `[ScaffoldReadOnly]` `[ScaffoldReference]` column lists the raw key**, since the emitter
  builds its label lookups from the writable choices only; a writable reference shows the
  target's label. Nothing in the spec says which is right for a read-only one. Found in the 023
  review.
- **`AHeldKeyThatIsNotOffered_IsStillRenderedTicked` asserts two emitted strings and no tier
  renders a form whose held key is past `OptionLimit` or absent from the target store.** The
  SQL-side tag-form case brushes it only because the test table holds ~50k articles. Found in
  the 023 review.
- **Migration 0004's backfill guard reads `information_schema.columns` by `table_name` alone**,
  with no `table_schema` predicate; an `articles` table in another schema would satisfy it.
  Applied as written to the rehearsal copy; the real databases still await the same file.
  Found in the 023 review.
- **`/articles.csv` gained `published` as column 6, before `tags`**, which moved from 6 to 7; a
  positional consumer of the 022 layout breaks silently. Nothing versions the CSV columns.
  Found in the 023 review.
- **The public output cache's identity check is the flash cookie alone** (`PublicPageCache.
  CarriesIdentityCookie` reads `__Host-nw-flash`, not the signin cookie), so a signed-in admin
  reads the same sixty-second-old home and tag pages as everyone else; the tests read public
  pages with an `Authorization` header to get past it. Whether an admin should see fresh pages
  is a product question. Found while building 023.

## Found by the 022 code review, 2026-09-03 — noted, not fixed

- ~~**The admin form posts the view count back as a value on both store paths**, so a view
  counted between the edit form's GET and its POST is lost: the memory store replaces the
  instance, `Update.sql` writes the posted number. Taking `ViewCount` out of the form
  (`[ScaffoldReadOnly]`) closes it; that is the next spec's admin editing work, and
  `JauntyStoreTests` posts `viewCount` today. Found in the 022 review.~~ **Closed 2026-09-03 by
  spec 023:** `ViewCount` is `[ScaffoldReadOnly]`, neither store takes it from the form, and
  `AdminRelationsTests` counts three visits through an open form on both paths.
- **`/articles/{slug}` is an anonymous GET that writes a row**, rate-limited at 120 a minute per
  client and uncached. Inflatable by anyone with a loop and a few addresses, and a hot article
  serialises on its row lock. Decided for 022 ("count it, page rendered per visit"); a product
  that cares would count asynchronously or dedupe by client. Found in the 022 review.
- **`MemoryPublishedArticles.ScanLimit` (1000) bounds the store read before the published
  filter**, so past a thousand articles the in-memory home, tag pages, sitemap and feed silently
  omit rows and their article pages 404. Demo-store only; the constant is documented as
  arbitrary. Found in the 022 review.
- **The sitemap lists every published row with no cap**; the format's ceiling is 50,000 and the
  test database is at ~46k articles and accumulating. A sitemap index is the fix when it matters.
  Noted on the branch, confirmed by the review.
- **The view is counted before the row is read**, on two connections on the SQL path. An
  unpublish landing between them answers 404 with a view already counted. Reversing the order
  moves the same window to the other side. Found in the 022 review.
- **Migration 0003 seeds tags but no `article_tags` rows on the database path**, since it seeds
  no articles either; a fresh database shows three tags with zero counts, where the memory seed
  links `a1` to two of them. Found in the 022 review.
- **`SET NOT NULL` and `CREATE UNIQUE INDEX` in 0003 take full-table locks**; on the 46k-row test
  database that was seconds, on a live one it is a write outage for the duration. `CONCURRENTLY`
  for the index if this pattern ever reaches a product's migration. Found in the 022 review.
- ~~**Deleting a tag cascades its links on the database path and orphans them in memory**, where
  the readers filter dangling slugs out. Same page either way today; re-creating the tag revives
  the memory links and not the SQL ones. Spec (b) decides which is right when it adds relation
  editing. Found in the 022 review.~~ **Closed 2026-09-03 by spec 023:** the memory relation
  drops a deleted tag's links the way the cascade does, so a re-created tag starts with none on
  both paths.
- **The derived-slug retry loops in `JauntyArticleStore.CreateAsync` and `Slugs.Unique` are
  unbounded**: nine hundred articles titled alike cost nine hundred failed INSERTs on the next
  create. Admin-only input; a cap with a refusal message is the fix. Found in the 022 review.
- **`Slugs.IsValid` has no length bound.** A pasted 4 KB slug fails the btree index insert with
  a `PostgresException` that is not a unique violation, which escapes as a 500 rather than the
  refusal message a bad character gets. Found in the 022 review.
- ~~**`TagStore.AssignKey` overwrites a typed tag slug with a generated hex key** on the memory
  path, so the two paths disagree about whether an admin can choose a tag's slug. Spec (b).
  Found in the 022 review.~~ **Closed 2026-09-03 by spec 023 (R9):** both stores keep a valid
  typed slug and generate one only for a blank; the form never carries the key, so the fact is
  proved at the store seam.
- **`/articles.html` is still a second, untested URL for the listing** (carried from the 021
  review); `/articles/{slug}` sits beside it and neither canonicalises the other.

## Found while building plan 009 (sample content and the visual refresh), 2026-09-03

- **No public page names an article's category.** `PublishedArticle` has no category field, so
  the list, the article page, the JSON and the CSV all omit it while the admin edits it. Adding
  it is not one label: it touches both readers, `ListPublished.sql` and its companions, the JSON
  contract and the CSV column order (which the 023 review already flagged once for a shift), and
  every test that constructs the record. Deliberately out of plan 009's scope; worth doing as its
  own change, appending the field so no positional consumer moves.
- **`nativeweb_sample_test` holds 50,641 article rows**, every one dated 2026 and left by keyset
  and mutation runs -- the 017 cleanup removed 100,980 in September and the table filled again.
  Nothing fails because of it, but it is why no suite can assert an exact row count over the
  database path, and why `SeedParityTests` had to compare the two seed *files* rather than the
  two stores. Listed in `scripts/cleanup/009-sample-content-and-visual-refresh.ps1`.
- **The tag pill's colour dot is `currentColor`, not the tag's own colour.** `Tag.Color` is in
  the store and the seed sets three distinct values, and the public `TagRef` carries only slug
  and name -- so the dot repeats the text colour. Same read-model widening as the category note
  above, and worth doing in the same pass.
- **`apps/sample` hardcodes its asset URLs; the skeleton resolves them.**
  `apps/sample/templates/layout/page.html` and `Render/ArticlesPage.cs` write
  `/_nativeweb/css/tokens.css` literally, while `skeleton/apps/__product__` writes
  `{{Assets.Resolve("/_nativeweb/css/tokens.css")}}`. The literal form works but is served off the
  identity route, so the sample gets `Cache-Control: no-cache` where the fingerprinted route would
  be cacheable for a year -- and `AssetUrls` exists to make that impossible to get wrong. The
  reference app demonstrating the wrong one of the two is the part worth fixing.
- **`tools/contrast-check.ts` is a manual gate.** Nothing runs it -- not CI, not a test, not a
  hook -- so the palette's WCAG guarantee holds only as long as someone remembers the command
  after editing `sample.css`. A CI step needs `denoland/setup-deno`, a second `uses:` action that
  decision `2026-08-20-001` argued against; porting the ratios into
  `tests/nativeweb.repo.tests.unit` would run them with every `dotnet test` and needs no new
  runtime. Worth doing; out of plan 009's scope.
- **The sticky topbar is measured against a background it does not have.** `.topbar` is
  `color-mix(in srgb, var(--s1) 82%, transparent)` over `backdrop-filter`, so the nav text sits
  on 82% of `--s1` over whatever scrolls beneath it -- a card, or the hero's `--ac-dim` wash.
  `contrast-check.ts` measures that text against opaque `--s1`, which is the best case rather
  than the rendered one. Compositing it properly means the script has to understand
  `color-mix()` and to know what can scroll under the bar.
- **`db/migrate.sh`'s `strip_sql_comments` cannot see a `--` inside a SQL string literal.** It
  strips from `--` to end of line, and `0005_seed_demo_content.sql` is the first migration whose
  prose bodies contain one (a4 and a7 both do). psql always receives the original file, so this
  can only cause a missed `@no-transaction` classification, never a wrong statement -- but the
  safety net is blind past the first `--` in any future seed migration's prose.
- **Nothing renders `articles/index.html`'s filter over the seeded set in a browser.** The e2e
  journey covers home, an article and a tag page; the htmx filter is proved at the integration
  tier only. It was already so before plan 009 -- noted because the list is now long enough for
  a filter to matter.
- **JauntyQ local-checkout mode is broken repo-wide, and not by anything here.** The sibling
  `../jauntyq` `dev` no longer has `src/JauntyQ.Generator` or `src/JauntyQ.Runtime` -- every
  project is now `Extrode.JauntyQ.*` -- so `EnsureJauntyQRoot` fails on every project and
  `NativeWeb.slnx`'s two direct references point at paths that do not exist. Every local build
  needs `-p:JauntyQUsePackages=true` until the probe, the sentinel filename and the two solution
  entries are updated to the new names. Found on the 024 branch, out of its scope.
- **The e2e tiers need `JauntyQUsePackages` in the ENVIRONMENT, not as `-p:`.** `SampleProcessFixture`
  shells out to a bare `dotnet publish`, and `-p:` on the `dotnet test` command line does not reach
  that child. `JauntyQUsePackages=true dotnet test <e2e project>` works, because MSBuild reads
  environment variables as properties. Worth a line in the fixture or the quick-start; found on the
  024 branch, out of its scope.
- ~~**Port 5348 was held by `justsmtp.web.exe` on 2026-09-03**, so `JauntySampleFixture` could not
  bind and all 31 scaffold e2e facts failed at fixture init.~~ **CLOSED 2026-09-05** on
  `fix/scaffold-e2e-os-allocated-ports`. No fixture names a port now; the five that did (5347,
  5348, 5351, 5352, 5353) all ask the operating system. 32/32 with justsmtp still listening on
  5348. The readiness loop also checks `HasExited` first, so a process that died answers with its
  exit code and log tail rather than a 60-second timeout.
- **`tests/nativeweb.signin.tests.aot` now gates two libraries, and its name says one.** Spec 024
  T19 added the permission layer to `probe-host` rather than standing up a second AOT tier,
  because a NativeAOT publish is ~21 s of link per tier and one host covers both. The tier wants
  renaming to `nativeweb.tests.aot`, which is a rename to ask about, not cleanup to do.
- **`nw_subject` grows without bound and the access index pages nothing.** Every first sight of a
  subject writes a row, the index lists them all, and neither has a bound. Fine at demo scale and
  not at a real one. Found by the 024 branch review; the fix is a paged subject list plus a
  retention decision nobody has made, so it is a spec of its own, not a line in this one.
- **`AccessAdmin` resolves one group by scanning `GroupsAsync()`.** Every group screen reads every
  group to find one. `IPermissionStore` has no `GroupAsync(long)` and adding one changes the seam,
  which is why the 024 review left it: a fourteenth method on a shipped interface is a decision.
- **`CHANGELOG.md`'s `[Unreleased]` section carries 34 `###` headings for 6 kinds.** Every merge
  appends its own `### Fixed` / `### Added` block rather than joining the existing one, so the
  section runs 1,800 lines with the same heading repeated a dozen times. Two adjacent `### Changed`
  headings were merged on 2026-09-06; the rest is a restructure nobody has asked for. Only worth
  doing when `dev` next merges to `main`, where the section becomes a version and has to be read.
- **`FormValueAsync` joins repeated form fields with a comma.** Posting `permission` twice yields
  `a,b`, which fails catalog validation rather than being rejected as malformed. Harmless today
  because the catalog check catches it; a wrong message for the wrong reason.
- ~~**The GitHub Packages credential is written into the checked-out `nuget.config` in clear text.**~~
  **Fixed 2026-09-05** on `fix/ci-credential-outside-workspace`, and the fix was itself broken
  until 2026-09-06 on `fix/ci-credential-config-file`: `add source --configfile` does not create
  the file it is given, so every credential step would have exited 1 on a runner with no
  user-level config. The guard was weaker than this note claimed, too -- it accepted a relative
  `NuGet.Config` and stayed green with a whole step deleted. Nine mutations are red on it now.
  Original note follows.
- **The GitHub Packages credential is written into the checked-out `nuget.config` in clear text.**
  `dotnet nuget update source jauntyq-github --store-password-in-clear-text` with no `--configfile`
  writes to the nearest config declaring that source, which is the committed one, so the token sits
  in the workspace for the rest of the job. Three workflow jobs do it -- ci.yml's `test` and
  `aot-publish` and now publish.yml, which is the one holding `packages: write`. Job-scoped and
  ephemeral, and the same pattern predates spec 028; the fix is `--configfile` pointing at a config
  outside the tree, or `dotnet nuget push --interactive` style env credentials. Found reviewing 028,
  out of its scope.
- **`PermissionModel` has no value equality**, so the generator's `Collect()` compares by
  reference and the incremental cache never hits across edits. A correctness non-issue and a
  build-time cost only.


## From the fable (review-deep) pass on spec 024, 2026-09-04

- ~~`IPermissionStore` has no way to delete a group or forget a subject.~~ **Promoted 2026-09-04
  to [spec 026](../docs/specs/026-permission-group-lifecycle/026-spec.md)**, `Status: draft`, 11
  requirements. The `ON DELETE CASCADE` clauses in `db/sample/migrations/0006_permissions.sql`
  exist for a delete no code path performs, and `PermissionConflictException` cites "a group that
  has since been deleted" as a state nothing can produce.
- ~~`PermissionContext.HeldAsync` reads the subject from `ClaimTypes.NameIdentifier` with no way
  for a host to name another claim.~~ **Promoted 2026-09-04 to
  [spec 025](../docs/specs/025-permission-subject-resolution/025-spec.md)**, `Status: draft`, 8
  requirements. A JWT bearer scheme with `MapInboundClaims = false` reaches it from stock
  configuration: every gated route answers 403 with nothing recorded to say the subject was never
  found.
- ~~`PermissionContext.HeldAsync` memoises on the first principal and ignores the `user`
  argument thereafter.~~ **Fixed 2026-09-04** on `fix/024-permission-followups`: held per
  principal, with a fact for a second principal and one holding the single store read.
- ~~`ScaffoldGenerator` sets `HasPermissions` on attribute presence alone, so a model declined by
  NWP001-007 lost its constants class and one real diagnostic arrived beside a CS0234 cascade.~~
  **Fixed 2026-09-04** on `fix/024-permission-followups` from the other end: the permissions
  generator emits the constants class for a declined model too, keeping it out of
  `PermissionCatalog`. **Still open, and narrower than the original finding:** a consumer that
  references `nativeweb.permissions` but not `nativeweb.permissions.generator` as an analyzer
  gets the same cascade, and no generator can see another generator's output to detect it.
- ~~`apps/sample/Program.cs` grants `PermissionCatalog.All` on the in-memory path while
  `0006_permissions.sql` seeds four literal `sample:article.*` names.~~ **Fixed 2026-09-04** on
  `fix/024-permission-followups`: `ArticlePermissions.All` on both, with `SeedGrantParityTests`
  parsing the migration's `VALUES` list and comparing it to the generated constants.
- `libs/nativeweb.render.generator/RenderTemplateGenerator.cs:52` — the comment says a
  same-logical-path collision "falls out as NWT009 via class-name collision". It cannot: line 54
  does `models[path] = …`, so the second file replaces the first before the emitter runs, and
  NWT009 only fires for *different* logical paths colliding on a class name. Found reviewing spec
  029, whose `NativeWeb.Web.targets` states the real behaviour; out of that spec's scope to fix.
- `NativeWeb.Web.targets` scopes its clash check to `$(MSBuildProjectDirectory)/templates`, but the
  generator keys on the last `/templates/` segment of *any* `AdditionalFiles` path. A consumer
  whose templates arrive through some other glob can still collide with a framework template
  silently.
- Nothing marks `libs/nativeweb.web/templates/**` as a watched input for a consumer, so an edit to
  a framework template does not make `dotnet watch` or the IDE's up-to-date check rebuild the
  product. `apps/sample/nativeweb.sample.csproj` has a `<Watch>` for its own templates and there is
  no equivalent for the framework's.
- The design system exists in three byte-identical copies. `scripts/cleanup/015-framework-web-seam.ps1`
  removes the two that spec 015 superseded and has never been run.
- `scripts/cleanup/fable-review-findings.ps1` globs `*fable-review*`, which is wider than the
  synopsis says it removes.
- xUnit1051 (`xunit.analyzers` 2.0.0, arrived with xunit.v3) is suppressed in
  `tests/Directory.Build.props` and `skeleton/tests/Directory.Build.props`. It wants
  `TestContext.Current.CancellationToken` on every call that takes one - 468 distinct sites here.
  Satisfying it changes what the tests do, not how they build, so it belongs in its own branch.
- ~~`nativeweb.signin.tests.e2e` fails 4/4 on this workstation~~ **CLOSED 2026-09-05** on
  `fix/signin-e2e-excluded-port`. Both 5349 and 5350 are in the excluded ranges, on the IPv6 list
  as well as the IPv4 one, so the bind was refused with nothing listening. The fixture asks the
  operating system for two ports instead of naming any, which the OS never allocates out of an
  excluded range. The tier runs 5/5. The scaffold, forms and permissions tiers got the same fix on
  `fix/scaffold-e2e-os-allocated-ports` the same day, and it corrected this one: both allocators
  probed `TcpListener.Create(0)`, the dual-stack wildcard, which on Windows coexists with a
  listener already on 127.0.0.1 -- so the OS could hand out a port Kestrel then could not bind.
  Both probe the two loopback addresses now, and since 2026-09-05 they decide whether to probe
  `::1` by binding it once rather than by reading `Socket.OSSupportsIPv6`, which reports the stack
  and not the address -- justsmtp.com's finding, on `fix/ipv6-loopback-trial-bind`.
- Two cleanup scripts both claim `tmp/sample-publish.lock`, and they disagree about the guard:
  `scripts/cleanup/signin-e2e-excluded-port.ps1` removes it unconditionally, while
  `scripts/cleanup/scaffold-e2e-os-allocated-ports.ps1` removes it only when nothing holds it.
  Running the first while an e2e tier publishes would let a second publish race it, which is the
  ILC failure the lock exists to prevent. Neither has been run. Noticed 2026-09-05 while dry-running
  all three; `ipv6-loopback-gate.ps1` deliberately does not touch the lock.
- `libs/nativeweb.signin.testing` carries `[Fact]`-bearing conformance suites and sits under
  `libs/`, outside the xUnit1051 suppression the two `tests/Directory.Build.props` files added. It
  builds clean today, so nothing is broken; the first call site there that takes a
  `CancellationToken` becomes a build error rather than a warning.
- ~~`tests/nativeweb.repo.tests.unit/PackageFeedConfigTests.cs:234` names `apps/web/nativeweb.web.csproj`
  in a comment as the path its bounded regex must not swallow.~~ **CLOSED 2026-09-05**: the regex
  and its comment both went with `SolutionRemovePattern`, which existed only to check the
  `dotnet sln remove` step that the JauntyQ split made unnecessary.


- ~~`Directory.Build.props:118` probes `$(JauntyQRoot)\src\JauntyQ.Generator\JauntyQ.Generator.csproj`,
  but the jauntyq checkout has renamed its projects to `Extrode.JauntyQ.*`~~ **CLOSED 2026-09-05,
  and the diagnosis above was wrong.** It is not a rename: JauntyQ **split**, moving its generator
  and runtime to a public core repository that is not checked out here at all, so `../jauntyq`
  carries only the premium half. Package mode is the default now
  (`docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md`) and a bare `dotnet build`
  works again.

  **What is still open from it**, and both need a decision rather than work:
  - The `EnsureJauntyQRoot` sentinel in `Directory.Build.props` and in
    `skeleton/Directory.Build.props` still names the pre-split csproj. Whoever clones the public
    core repoints both, along with the two `ProjectReference` pairs, in the same commit. Not
    guessed here: a probe pointing at an unverified path is the `extrode-legacy-org.com` breakage
    `JauntyQRootProbeTests` exists to prevent.
  - `AGENTS.md` says "JauntyQ is used from a local checkout only." The default build resolves
    packages now, so that no longer describes how we build. It is a licence statement, left
    verbatim for the user to rule on.

- **`tools/status-roll.js` moves bodies but not headings.** After a roll, `current.md` opens
  `# Next` and the archived `work/done/<NNN>-*.md` opens `# Previous`, each carrying the
  rotation boilerplate of the file it came from. Rewritten by hand on 2026-09-05; the tool lives
  in `~/.claude/tools/`, so fixing it is config work, not a change to this repo.

- **`backup.sh`'s dry run cannot distinguish an unreachable bucket from an empty one.** Line 111
  pipes `aws s3 ls` into awk under `set -o pipefail`, so the `|| echo "(bucket not listable from
  here)"` fires whenever the aws call exits non-zero -- which it does for a prefix that simply has
  no objects yet. Measured 2026-09-05: the message printed both when the bucket was genuinely
  unreachable and on the first-ever backup with the bucket listable and empty. The dry run exits 0
  either way. Not fixed here: the deploy shell scripts have no test harness, and changing one
  without a test that fails when the code is wrong is worse than the note.
- **The NativeWeb backup bucket is not offsite.** It is MinIO in the `nw-drill-s3` container on
  `incus-host`, the same physical host as `nativeweb-dev`. Losing that machine loses the host,
  the backups and the age key together. Stood up 2026-09-05 for the restore drill; a real bucket
  is an operator decision.
- **`BACKUP_HEARTBEAT_URL` is still unset on `nativeweb-dev`**, so R15 is unmet: a backup that
  stops running is invisible. `backup.sh` says so on every run.
- **`nativeweb-dev`'s database holds two drill marker rows**, `drill-pre-20260905` and
  `drill-post-20260905`. They are what made the restore drill falsifiable. Removing them is a
  database delete, so it is the operator's to run, not done here.
- **A stamped product's CI fails its second run on a persistent runner.** `skeleton/.github/workflows/ci.yml:188` and `:316` create the user-level NuGet config only when it is absent and never remove the source, so the next job's `dotnet nuget add source` exits 1 with "already been added". NativeWeb's own workflows write the file fresh every job since 2026-09-24; the skeleton was left alone because a stamped product's runner is its own choice. Found by Fable's review of `fix/ci-fresh-user-nuget-config`.
- **A stamped product's vulnerable package gate hides why its audit failed.** `skeleton/.github/workflows/ci.yml:219` still runs `out=$(dotnet list package ...)` bare, so a source the audit cannot query ends the step under `bash -e` with no reason in the log. NativeWeb's own gate captures `2>&1` and prints the output on failure since 2026-09-24 (`VulnerableGateTests`).
- **`SkeletonFrameworkResolutionTests.cs:336` remark names a pre-0.5.0 id.** It says the stamp step "dies NU1301/401 on JauntyQ.Generator"; the pins are `Extrode.JauntyQ.*` on nuget.org since 2026-09-24. Reword alongside the `skeleton/nuget.config` JauntyQ-pattern item.
- **`ci.yml` skeleton-stamp credential comment names the wrong config.** It says `update source` with no `--configfile` would write to "this repository's committed one"; the root config never declared `framework-github`, so the nearest config there is the stamped `ciprobe/nuget.config`. Found by Fable 2026-09-24; pre-existing.

- **JauntyQ license wording is stale in NOTICE.md:61-64, skeleton/AGENTS.md:58 and skeleton/.github/workflows/ci.yml:128.** They still say "self-hosted-SaaS use of generated output only" and "binary not shipped to third parties"; AGENTS.md, README.md and the quick-start now cite the Output and Redistribution Exceptions. NOTICE is legal text and the skeleton CI is off limits, so both wait for the user. Found by the 2026-10-03 public-release review.
- **`publish.yml` installs Deno by an unpinned `curl https://deno.land/install.sh | sh` inside the job that holds `id-token: write`.** `DENO_VERSION` pins the binary, not the script that fetches it, and neither is checksummed, so a compromised installer could mint a nuget.org key through OIDC. Fix: download the release zip directly and check a pinned sha256 (needs the real checksum, and the runner arch for `CI_LINUX_RUNNER`). Found by the 2026-10-22 review of 1738498..447e3c6.
- **The publish CI gate's verdict logic has no executable test.** `PublishWorkflowTests.TheCiGateReadsEveryCheckRunNotOnlyTheLatest` pins `filter=all` in the query, not the jq that picks the newest completed GitHub Actions run; it was checked by hand against six fixtures on 2026-10-22. Moving the gate into a Deno tool with a test would close it. Found by the 2026-10-22 review re-pass.
- **`FeedCredentialPattern` in `SkeletonFrameworkResolutionTests` reads one line at a time.** It misses `secrets: inherit`, `toJSON(secrets)` and a `dotnet nuget \` continued onto the next line. Found by the 2026-10-22 review re-pass.
