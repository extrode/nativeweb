# 011 — Production deployment of `apps/web`

Status: done — built and merged; R1 met on a real host 2026-09-01, R16 and R17a met by the drill
of 2026-09-05 on `nativeweb-dev` and `nativeweb-dr`. **Three requirements are unmet on that host
and none of them is code.** R14 and R17b: the bucket was a MinIO container on `incus-host`,
the machine it backs up, so a loss of that machine would have taken every backup with it. The
bucket is gone as of 2026-09-06, deleted deliberately by the drill's own cleanup script rather
than by any failure. R15:
`BACKUP_HEARTBEAT_URL` is unset, so a backup that stops running is unnoticed until it is needed.
All three are operator settings, carried in the runbook's gap table rather than left implicit.
**The second addendum of 2026-09-06 changes how all three will close** -- by moving this host onto
the `incus-host` fleet backup rather than by configuring `backup.sh` -- and records that
nothing is backing this host up in the meantime.
Created: 2026-08-27

## Problem

NativeWeb's application work is finished and green — every numbered spec is `done` or
deliberately held, 1,373 tests pass, the solution builds with zero warnings — and **none of it
can be run by anyone.** Slice 10 shipped its application half and its deploy half never started:
there is no host, no certificate story, no process supervision, no backup, no restore, and no
documented way to put a build in front of a user. The repo contains no deployment artifact of any
kind.

Two people hurt. The **operator** has nothing to run and no procedure to follow, so putting
NativeWeb in front of real traffic is an improvised, unrepeatable act whose failure modes are
discovered live. The **user whose credentials this holds** is exposed to the consequence: an
identity provider with no exercised restore procedure is one disk failure away from losing every
account it serves, and no amount of passing tests changes that.

This is separate from — and blocks nothing on — the open question of whether NativeWeb replaces
all of epass.io or only its browser surface. A host that runs the app, terminates TLS, survives a
reboot, and can be restored from a backup is required under either answer. That decision is
deliberately parked; this spec is the work that is true regardless.

## Goals

- An operator with shell access and this repo can bring a production host from bare OS to serving
  traffic by following one document, with no undocumented steps and no improvisation.
- Deploying a new build is one command, and a failed deploy leaves the previous version serving.
- The database can be restored from backup onto a clean host, and the time that takes has been
  measured rather than estimated.
- Nothing in production is retrying against infrastructure that does not exist, and nothing is
  serving a value hardcoded for a different deployment.
- Every secret the running service needs is delivered to it without being committed to the repo
  or left readable by other accounts on the host.

## Non-goals

- **The epass.io cutover scope decision.** ~~Whole replacement versus browser surface only stays
  open (`docs/architecture/002-epass-cutover-capability-gap.md`).~~ **Answered browser-surface-only
  on 2026-08-29 in the epass.io repo, and closed here entirely on 2026-08-31**
  (`docs/decisions/2026-08-31-008-epass-cutover-closed.md`). It stayed a non-goal throughout and
  nothing in this spec presumed an answer, which is why the closure changes nothing here.
- **Closing the capability gap.** No SMS OTP, no Telegram/WhatsApp, no `/oauth/userinfo`, no
  authenticator devices, no push approvals, no hosted background services. Those are the cutover's
  work, not the deployment's.
- **DNS cutover of the `epass.io` name itself.** Pointing production traffic at the new host is a
  user-gated act that happens after this spec and after the scope decision.
- **High availability.** One host. No clustering, no failover, no load balancing, no read
  replicas. A host loss is a restore, not a failover, and the spec states how long that takes
  rather than pretending it does not happen.
- **Point-in-time recovery.** Nightly granularity is the accepted recovery point; continuous WAL
  archiving is out of scope and recorded as a known limit, not an oversight.
- **CI-driven deployment.** No production credentials in CI, no runner reaching the host.
- **A permanent staging environment.** Restore drills need a throwaway target, not a standing
  second deployment.
- **Container orchestration.** Out of scope by the runtime decision below.

## Constraints (decided before this spec, not open)

- Single Linux host. The application runs under the host's own service manager (systemd) as a
  supervised unit; TLS terminates at Caddy in front of it. PostgreSQL runs on the same host.
- Deployment is initiated manually by an operator. Nothing deploys itself.
- Backups are nightly logical dumps, copied to storage on a different machine or provider, with a
  written and exercised restore procedure.
- The deployed artifact is the self-contained NativeAOT binary this repo already builds.

## User stories

- As an **operator**, I want to provision a production host by following one document, so that the
  result is the same every time and does not depend on what I remembered.
- As an **operator**, I want to deploy a new build with one command, so that releasing is not a
  sequence of steps I can get partway through.
- As an **operator**, I want a failed deploy to leave the site up, so that shipping is not a bet.
- As an **operator**, I want to know a backup failed on the morning it failed, so that I do not
  discover it during a restore.
- As an **operator**, I want to have already performed a restore before I need one, so that the
  first time is not during an outage.
- As a **user of the identity provider**, I want my account to survive the loss of a machine, so
  that a hardware failure is not an account deletion.
- As a **user**, I want every request to be encrypted in transit without the operator remembering
  to renew anything, so that security does not depend on a calendar reminder.
- As a **reviewer**, I want the production configuration to be readable in the repo, so that what
  is running is knowable without logging in.

## Requirements

Provisioning and runtime

- **R1** Host provisioning is reproducible from a bare OS install by following a single document,
  and every step in it is either a command to run or a decision with a stated default. No step
  reads "configure X appropriately".
- **R2** The application runs under a dedicated unprivileged account that owns no more than the
  files it needs, cannot log in interactively, and is not the account used to deploy.
- **R3** The service starts automatically on boot, restarts automatically after a crash, and stops
  attempting restarts when the failure is persistent rather than looping indefinitely.
- **R4** All public traffic is encrypted. Certificates are issued and renewed without operator
  action, and plaintext HTTP requests are redirected rather than served.
- **R5** Only the ports required to serve traffic and administer the host are reachable from the
  public internet. The database is not among them.
- **R6** Logs are bounded. No log source can fill the disk, and the retention period is stated.
- **R7** The host applies security updates on a documented cadence, and whether that is automatic
  is a recorded decision rather than an omission.

Deploying and rolling back

- **R8** A new build is deployed by one documented command. The command is safe to re-run: running
  it twice in a row leaves the same state as running it once.
- **R9** Deployment applies any pending database migrations before the new binary serves traffic,
  and refuses to proceed if a migration fails.
- **R10** A deployment whose new process does not become ready leaves the previous version
  serving. Readiness is determined by the application's own readiness signal, not by elapsed time.
- **R11** The previous release can be restored by one documented command, without a database
  restore, for any release that did not change the schema. Releases that did change the schema
  state that they cannot be rolled back this way.
- **R12** The artifact an operator deploys is built from a known commit, and which commit is
  running is discoverable on the host without guessing.

Data durability

- **R13** A full logical backup of the production database is taken nightly without operator
  action.
- **R14** Backups are copied to storage on a different machine or provider from the one being
  backed up, are encrypted at rest there, and their retention period is stated.
- **R15** A backup that does not happen, or happens and fails, produces a signal the operator
  receives. Absence of a signal never indicates success.
- **R16** The restore procedure is documented and has been executed end to end against a
  non-production target, and the time it took is recorded as a measured number.
- **R17a** Rebuilding a working production host from this repository plus a reachable backup
  is documented as a single procedure, and the total time that takes is stated.
- **R17b** The backup that rebuild reads survives the loss of the host it was taken from.
  Split from R17 on 2026-09-06; the original wording assumed an offsite backup that does not
  exist, so one requirement was being reported met on evidence for half of it.

Configuration and secrets

- **R18** No secret the running service needs is committed to this repository, and no secret is
  readable by accounts on the host other than the service account and the operator.
- **R19** Starting with a missing or malformed required configuration value fails immediately and
  says which value, rather than starting and failing on first use.
- **R20** The deployment's canonical public hostname is configuration. Any content that names the
  site derives from it, and no host-specific value is compiled into the application.
- **R21** Telemetry export is either pointed at a collector that exists or explicitly disabled. No
  deployment is left with export enabled and no destination.
- **R22** The production build is produced by a dependency-resolution mode that is known to work,
  and if that differs from the mode CI uses, the difference is documented rather than discovered
  at deploy time.

## Acceptance criteria

- **R1** — Given a bare OS install and the provisioning document, when a person who did not write
  it follows it start to finish, then the host serves the application over HTTPS with no step
  requiring a judgement call the document did not anticipate.
- **R2** — Given a running production host, when the service account is inspected, then it has no
  interactive shell, and attempting to write outside its own data and log paths is denied.
- **R3** — Given a running service, when the host is rebooted, then the service is serving without
  operator action. When the process is killed, then it is serving again without operator action.
  When it fails to start repeatedly, then restart attempts stop and the failure is visible.
- **R4** — Given a fresh host and a DNS name pointing at it, when the site is first requested over
  HTTPS, then a valid certificate is served with no operator having run a certificate command.
  Given a plaintext HTTP request, then it is redirected to HTTPS.
- **R5** — Given the production host, when its listening ports are probed from off-host, then only
  the documented public ports respond and the database port does not.
- **R6** — Given the retention period has elapsed, when the log directory is measured, then it is
  bounded and older entries have been removed.
- **R8** — Given a host serving version A, when the deploy command is run twice with version B,
  then the host serves version B and the second run changes nothing.
- **R9** — Given a pending migration that fails, when a deploy is attempted, then the deploy stops
  and the previous version is still serving.
- **R10** — Given a build that starts but never becomes ready, when it is deployed, then the
  deployment is abandoned and requests continue to be served by the previous version throughout.
- **R11** — Given a deployed release with no schema change, when the rollback command is run, then
  the previous version serves and no database action was required.
- **R12** — Given a running production host, when an operator asks which commit is deployed, then
  a documented command answers with a commit identifier that exists in this repository.
- **R13**, **R14** — Given a night passes with no operator present, when the offsite storage is
  inspected the next morning, then a backup from that night is present, is encrypted, and older
  backups beyond the retention period are gone.
- **R15** — Given the backup job fails or does not run, when the next check occurs, then the
  operator receives a signal saying so. Given the monitoring itself stops, then that is also
  visible.
- **R16** — Given a backup file and a clean database, when the documented restore procedure is
  followed, then the application starts against the restored data, serves a row that existed when
  the backup was taken, and does not serve a row written after it. The elapsed time is written into
  the procedure as a measured value.
  **Amended 2026-09-05.** It read "serves an authenticated session for an account that existed at
  backup time". `apps/sample` keeps no accounts in the database -- its only sign-in is a passphrase
  in `secrets.env` -- so that criterion passed against the restored database, an empty database and
  no restore alike. The replacement can fail, and did fail on the first attempt: the older release
  running on the idle colour had no `/articles/{slug}` route and answered 404 for both rows.
- **R17a** — Given only this repository and a backup reachable over the object-store protocol,
  when the disaster recovery procedure is followed, then a serving host exists, and the document
  states how long that took.
- **R17b** — Given the host that took the backup is gone, when the backup is read from the
  storage holding it, then it is still readable. No rehearsal of the disaster recovery
  procedure is required to close this: what R17b asserts is where the bytes live, which is
  what R14 asserts, and the DR run against them is R17a.
  **Split from R17 on 2026-09-06.** R17 read "Given only this repository and offsite backups",
  and the drill of 2026-09-05 had no offsite backup: the bucket was a MinIO container on
  `incus-host`, the same physical host as the guest being restored. What the drill proved is
  R17a, and reporting that as R17 claimed survival of a loss that was never rehearsed. R17b is
  R14 measured on a real host rather than a second requirement -- close R14 and R17b closes with
  it.
- **R18** — Given the repository at any commit, when it is searched for production secret values,
  then none are found. Given a non-privileged account on the host, when it attempts to read the
  secret material, then it is denied.
- **R19** — Given a required configuration value is missing, when the service is started, then it
  exits with a message naming that value, and does not begin serving.
- **R20** — Given the canonical hostname is changed in configuration, when the site is requested,
  then content naming the site reflects the new value with no code change and no rebuild.
- **R21** — Given a deployed host, when telemetry configuration is inspected, then either an
  endpoint is set and reachable, or export is disabled. Neither is left to the default.
- **R22** — Given the documented build procedure, when it is run on a machine with no prior state,
  then it produces a deployable artifact, or it fails with a documented reason and a documented
  alternative.

## Known limits, accepted

- **Up to 24 hours of data can be lost.** Nightly backups are the decided recovery point; there is
  no continuous archiving. Stated here so it is a decision on record rather than a surprise.
- **A host loss is an outage, not a failover.** Single host by decision. R17a bounds how long the
  outage lasts; nothing shortens it to zero.
- **The application and the database share a failure domain.** Same host by decision.

## Acceptance review — 2026-08-27

**Status stays `draft`. Nineteen of twenty-two requirements are met; three are not, and none of
the three can be met from a session.** The gap is not code — every script, unit and document
exists and is tested — it is that **no host has been provisioned, no backup restored, and no
disaster recovery exercised**. T15 is the user's to run, and 011 does not reach `done` until the
three measured times are in the runbook.

| Met | Not met |
| --- | --- |
| R2–R6, R8–R15, R18–R22 | **R1** provisioning walkthrough — written, never executed (T15) |
| R3 measured, not reasoned — see T14 | **R16** restore drill — the script is driven by unit tests; no real backup has been restored (T15) |
| R7, R12, R17 documented; R17's *execution* is below | **R17** disaster recovery — the procedure is written; nobody has rebuilt a host from repo plus backup (T15) |

## Acceptance addendum — 2026-09-01

**R1 is met. Status stays `draft` on R16 and R17.** `nativeweb-dev` — Debian 12, an Incus guest
on `incus-host` — was provisioned and then deployed to from `build.sh` and `deploy.sh`, and
the result is public at `https://nw-dev.example.com`: `/healthz` 200, `/readyz` 200 `ready`,
`/version` `0.1.0+7436517…`. The promotion took **1.774 s** and that figure is in the runbook.

The run needed four attempts, and **two of the three failures were defects in these scripts** —
which is the whole argument for T15 being a person's task rather than a document. Both are fixed
with tests:

- `ConditionPathExists` was in `[Service]`, where systemd logs "Unknown key name" and ignores it.
  The silent-skip guarantee R2 rests on had never held on any host.
- `chmod -R g-w,o-rwx` on the release directory is subtractive, and `cp -a` from a `mktemp -d`
  stage stamps 0700 onto it, so it could not restore bits that were never granted. The unit
  failed at `status=200/CHDIR`.

The third refusal was the preflight working: the host carried `NativeWeb__*` keys from a
provisioning run predating the config prefix, and `deploy.sh` named them and stopped.

Two caveats on R1 specifically. The host was provisioned on 2026-08-30 under the previous
configuration prefix rather than from a bare host in one pass, so the **walkthrough as written**
has still not been followed start to finish in a single sitting; and this is a dev host, which is
the only thing the user authorised. `nativeweb-prod` has never been provisioned.

Four caveats that survive the ticks:

- **R15 is conditional and currently unmet on any real host.** The dead-man's-switch is
  implemented and the failure unit is installed, but `BACKUP_HEARTBEAT_URL` is unset until an
  operator points it at an external check. `backup.sh` says so in its own output on every run
  rather than letting the gap pass quietly. A backup that silently stops running is invisible
  until it is needed.
- **R3 was verified on the wrong kernel.** `MemoryDenyWriteExecute=yes` was measured under WSL2,
  not on the target host. Same seccomp filter, different kernel; detail in T14 and the unit file.
- **R22 was exercised, and it found two defects.** The first real `build.sh` run compiled a
  34,632,768 B `linux-x64` binary and then died on `tar --zstd` with no `zstd` installed —
  four minutes spent before a preventable failure, and a truncated 4,096-byte artifact left at
  the path `deploy.sh` is told to ship. Both are fixed (tool preflight ordered after the
  Windows-shell refusal; the EXIT trap widened over the artifact and narrowed again after tar
  returns 0) and both are pinned by tests.
- **The two-lens review found fifteen real defects in this branch's own work, and
  they were the reason for running it.** Both lenses independently put the same one
  first: `deploy.sh` invoked `db/migrate.sh` with no environment, so it inherited the
  *development* database and role — `nativeweb_dev` and `nativeweb_app`, neither of
  which `provision.sh` creates. **Every first deploy on a provisioned host would have
  aborted at the migration step.** Four more of that kind: the nightly backup ran as
  root with no PostgreSQL role called root, so it would have failed on the first
  timer run of every host; both preflight guards checked the two keys `provision.sh`
  seeds *populated* and so could never fire, while the keys it seeds blank went
  unchecked; `StartLimitIntervalSec` and `StartLimitBurst` sat in `[Service]`, where
  systemd ignores them, leaving the crash-loop ceiling the comment describes
  non-existent; and `live_colour` read the first port-shaped string in the upstream
  file, which was a comment, so it would have called the live colour idle and
  deployed over the release that was serving. Also fixed: `rm -rf` on the release
  directory a live colour is running out of, no deploy lock, root executing a
  predictably-named script out of world-writable `/tmp`, a masked schema-count probe
  that recorded a migrating release as non-migrating (defeating R11's whole guard), a
  global SSH rate limit that an attacker can exhaust to lock the operator out,
  `provision.sh` flushing the host's entire nftables ruleset on a re-run, three
  guards made unreachable by `pipefail`, `restore.sh` comparing against a hardcoded
  production name and stripping the grants the app needs, a prune failure reported as
  a successful backup, and `CanonicalBaseUrl` returning the raw string so
  `https://user:pass@host` would have been published in the sitemap. Two vacuous test
  assertions were fixed as well — `IndexOf` returns -1 and `-1 < n` is true, so an
  ordering check alone passed when the thing it ordered was gone.
- **R19 needed no work.** `StartupConfigGuards` already exits 78 naming the missing value; this
  spec added `NativeWeb:CanonicalBaseUrl` to the same guard and `RestartPreventExitStatus=78` to
  the unit, so a config error is not restarted into a loop.

## Acceptance addendum — 2026-09-05, amended 2026-09-06

**R16 and R17a are met. R14, R15 and R17b are not.** R14 and the undivided R17 were being
reported as met before this addendum; R15 was already on record as unmet, in the status line
above and in the gap table of the runbook. The drill ran on `incus-host`: backup **1 s** (10,359 bytes, the
first successful backup this project has taken), restore into a scratch database **1 s**, verified
serve against restored data **1 s**, bare guest to serving **345 s**. `nw-dev.example.com` served
200 throughout and its live colour never changed.

| Requirement | Verdict | On what evidence |
| --- | --- | --- |
| R16 | met | A real backup restored into `nativeweb_drill`, verified by serving a row that existed at backup time and 404ing one written after it |
| R17a | met | `nativeweb-dr` built from repository plus backup, bare guest to serving in 345 s |
| R14 | **unmet** | The bucket is a MinIO container on the host being backed up. Nothing is on a different machine or provider, which is what R14 asks for |
| R17b | **unmet** | Follows from R14. The drill rehearsed recovering a broken guest, not losing the box |
| R15 | unmet | `BACKUP_HEARTBEAT_URL` unset, unchanged by the drill |

**Two things the drill did not prove, beyond R14.** The release artifact was repackaged from the
surviving host rather than built from source, because `build.sh` needs the JauntyQ feed credential
CI is still blocked on -- so the rebuild leaned on the machine it was meant to survive. And the
procedure gained steps 3 to 6 only by being run: the new host needs its own bucket credentials,
`age` and the AWS CLI, an allowance at the object store, and the app role's password set by hand,
since `pg_dump -Fc` carries no roles.

**The amendment.** This addendum first recorded R17 as met. R17's criterion said "Given only this
repository and offsite backups" and there were no offsite backups, so it was split into R17a and
R17b on 2026-09-06 and R14 was corrected from met to unmet. Found by a review of the branch that
recorded the drill.

## Acceptance addendum — 2026-09-06, the fleet takes over

**R14, R15 and R17b will not be closed by this project's own `backup.sh`.** `incus-host`
grew a fleet backup of its own on 2026-09-06 (`the host's ops runbook`):
backup-tool, `pg_dump -Fc` per VM nightly into **two** independent repositories -- offsite object storage and a
offsite storage -- plus a weekly whole-instance export, a daily `backup-tool check
--read-data-subset=10%`, and a dead-man probe every six hours. It answers R14, R15 and R17b
together and better than one more `backup.env` would, and its own runbook says not to stand a
second system beside it.

`nativeweb-dev` was in none of that system's three VM lists -- `TIER1_VMS`, `TIER3_VMS`,
`TIER3_IGNORE` -- so this project was invisible to it. Adding it to the first two is two words in
`ops/{db-dump.sh,vm-export.sh}`, committed on
`chore/nativeweb-dev-into-host-backup` in the host's ops repository, **merged to their `dev` the
same day**. Not in effect: `/opt/host-backup` on `incus-host` still runs the pre-review
scripts, and redeploying it is a founder call, so nothing about this host's backups has changed on
the fleet yet.

**Two words may not be enough, and this is not knowable from a checkout.** Both lists are
`${TIER1_VMS:-...}` defaults read after `load_env`, so a `/etc/host-backup/env` on the host that
sets either variable makes the change a no-op there. Whether it does, and whether the units run
the branch's copies, are host facts.

That absence was also failing their tier-3 coverage check, which exits before the export loop and
so fails the job for **every** instance on the host, not only for this one. Tier 3 runs Sundays
06:00 CEST and 2026-09-06 is a Sunday, so this morning's run is likely to have already failed with
three unlisted instances -- `nativeweb-dev`, and the `nativeweb-dr` and `nw-drill-s3` the drill
cleanup removed later the same day. Whether it did is in the host's journal.

| Requirement | Verdict | Changed by |
| --- | --- | --- |
| R14 | **unmet, closing** | Fleet tier 1 puts the dumps on offsite object storage and a offsite storage, both off `incus-host`. Unmet until a fleet run has captured this VM |
| R17b | **unmet, closing** | Follows R14, and additionally needs a restore from one of those repositories to be drilled. Neither has happened |
| R15 | **unmet, closing** | The fleet's freshness probe replaces `BACKUP_HEARTBEAT_URL` for this host. Its own runbook warns that a green marker means the job ran, not that this database was in it -- so the first run must be read, not assumed |

**Four things here are host facts and none has been checked**, because a checkout cannot see them:
whether `/etc/host-backup/env` overrides either VM list; whether the units execute the branch's
copies of the scripts; whether the tier-3 unit has already failed on the unlisted instances; and
whether `incus exec nativeweb-dev -- sudo -u postgres psql` answers, which tier 1 needs and which
`db-dump.sh` treats as a **fleet-wide** failure rather than a per-VM one -- its `exit 1` on
zero databases sits inside the loop, so a stopped `nativeweb-dev` aborts tier 1 for every sibling
before backup-tool runs.

**Nothing is backing up `nativeweb-dev` between these two addenda.** The MinIO container holding
every backup taken since 2026-09-05 was deleted on 2026-09-06 by
`scripts/cleanup/011-t15-restore-and-dr-drill.ps1 -RemoveBucket`, and the fleet has not yet run
against this VM.

The 03:17 timer is still enabled and fails nightly against an endpoint that no longer answers.
**It is inert, not a safety net**: every run dies at the upload and `backup.sh`'s `EXIT` trap
removes the dump, so it contributes no recovery point and disabling it would widen no window. It
stays enabled only because disabling it is a host action, batched with the retirement rather than
done for its own sake.

**What the fleet carries that `backup.sh` did not, and what neither carries well.**

`provision.sh` seeds five files on the VM's filesystem and none is in any repository:
`app.env`, `secrets.env`, `blue.env`, `green.env`, `backup.env`. The first two are the ones DR
step 5 carries by hand. **`backup.sh` never backed up any of them** -- it ships `pg_dump` output
and nothing else -- so on paper the fleet takes them from no backup at all to the weekly tier-3
tar, on offsite object storage alone: 7 days on one provider, against 1 day on two for the data.

**On the host, tier 3 has never produced a snapshot.** `incus list` returned 25 instances on
2026-09-06 against 15 classified, and the coverage check exits before the first export, so the
whole tier has never run. Ten instances need a founder call before it can. Until then the figure
for these five files is not seven days -- it is no backup, and the fleet's own `work/todo.md`
records it.

The `age` key is not in this list: it has been in the operator's off-host store since 2026-09-06,
and once the MinIO objects were deleted there is nothing left for it to decrypt.

Gained: tier 1 runs `pg_dumpall --globals-only` per VM, which `backup.sh` does not. DR step 6 sets
the app role's password by hand because `pg_dump -Fc` carries no roles; for this host `globals.sql`
now carries it. The fleet runbook restores globals to a scratch directory and warns against
applying it to a live cluster unread, so step 6 stays the default and `globals.sql` is the
cross-check rather than the replacement.

## Open questions

None. The scope decision this work deliberately excludes is tracked in
`docs/architecture/002-epass-cutover-capability-gap.md` and is not a question this spec needs
answered.
