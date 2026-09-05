# 011 — Tasks

Plan: ./011-plan.md
Spec: ./011-spec.md

Order is dependency order. T1–T4 are the only application code and are independently verifiable by
the existing test tiers; T5–T12 are the operator surface; T13–T15 are what a machine cannot assert
for itself.

## Application change (R20)

- [x] **T1 `NativeWeb:CanonicalBaseUrl` option and validation** — files: `apps/web/Program.cs`,
  new `apps/web/CanonicalBaseUrl.cs`, `tests/nativeweb.web.tests.unit/` — covers: R20 —
  done when: an absolute `http`/`https` value binds; a malformed value fails startup naming the
  key, in the same posture as the other validated keys; unset falls back to the request's own
  scheme and host so a dev checkout is unaffected; unit facts cover all three and the malformed
  case fails when the validation is removed.

- [x] **T2 Serve `robots.txt` and `sitemap.xml` from the configured base URL** — files:
  `apps/web/Program.cs` (or a new endpoints file beside the existing ones), delete
  `apps/web/wwwroot/robots.txt` and `apps/web/wwwroot/sitemap.xml`,
  `tests/nativeweb.web.tests.unit/` — covers: R20 — done when: both paths emit their current
  content with `https://epass.io` replaced by the configured base URL; the `Disallow` list is
  unchanged; both remain cacheable per spec 006's posture; no `epass.io` literal remains anywhere
  under `apps/web`.

- [x] **T3 Integration and e2e coverage for both endpoints** — files:
  `tests/nativeweb.web.tests.integration/`, `tests/nativeweb.web.tests.e2e/` — covers: R20 —
  done when: `WebApplicationFactory` against real PostgreSQL asserts the emitted URLs follow
  configuration and the cache headers hold; a Playwright fact fetches both from a running app
  configured with a non-epass.io base URL and asserts no `epass.io` literal survives.

- [x] **T4 Document the key** — files: `docs/02-reference/configuration.md` — covers: R20 —
  done when: `NativeWeb:CanonicalBaseUrl` appears with its default and validation posture, and the
  file's stated invariant — every `NativeWeb:` key the source reads, and no key it does not — still
  holds.

## Build and unit definitions

- [x] **T5 `build.sh` — publish `linux-x64` from WSL** — files: `scripts/deploy/build.sh`,
  `tests/nativeweb.web.tests.unit/` — covers: R12, R22 — done when: publishing produces
  `dist/nativeweb-<sha>.tar.zst` and a `RELEASE` file carrying the commit SHA and build time;
  building from a dirty tree is refused unless forced; the script names checkout mode explicitly
  rather than inheriting it by accident; unit facts cover the dirty-tree refusal and the RELEASE
  contents.

- [x] **T6 Hardened systemd template unit** — files: `scripts/deploy/systemd/nativeweb@.service` —
  covers: R2, R3, R18 — done when: instance name selects the loopback port (blue 5011, green
  5012); the unit sets `NoNewPrivileges`, `ProtectSystem=strict`, `ProtectHome=yes`, `PrivateTmp`,
  an empty `CapabilityBoundingSet`, `RestrictAddressFamilies`, and `ReadWritePaths` limited to
  `/var/lib/nativeweb`; `Restart=on-failure` with a start limit that stops a persistent failure
  looping; secrets arrive by `EnvironmentFile` so they do not appear in `systemctl show`.

- [x] **T7 Caddy and firewall configuration** — files: `scripts/deploy/caddy/Caddyfile`,
  `scripts/deploy/caddy/upstream.caddy.example`, `scripts/deploy/nftables/nativeweb.nft` —
  covers: R4, R5, R6 — done when: Caddy serves the canonical hostname with automatic HTTPS,
  redirects plaintext HTTP, proxies to the imported upstream snippet, and rolls its access log with
  a stated size and count; the nftables ruleset admits 22/80/443 inbound and drops the rest.

## Provisioning and release

- [x] **T8 `provision.sh` — bare OS to serving host** — files: `scripts/deploy/provision.sh`,
  `tests/nativeweb.web.tests.unit/` — covers: R1, R2, R5, R6, R7 — done when: the script creates
  the `nativeweb` system account with no interactive shell, installs and configures Caddy,
  PostgreSQL (`listen_addresses = 'localhost'`), nftables and `unattended-upgrades`, lays out
  `/opt/nativeweb` and `/var/lib/nativeweb/keys` with the right ownership, installs the units and
  timers, sets journald retention, and asserts `/etc/nativeweb/secrets.env` is `0640 root:nativeweb`
  on every run; running it twice changes nothing the second time; unit facts cover the idempotence
  and the permission assertion.

- [x] **T9 `deploy.sh` — stage, migrate, verify, promote** — files: `scripts/deploy/deploy.sh`,
  `tests/nativeweb.web.tests.unit/` — covers: R8, R9, R10, R12, R21 — done when: the script
  selects the idle colour, unpacks the release, runs `db/migrate.sh` and aborts on failure with the
  live colour untouched, starts the idle colour, polls its `/readyz` and abandons the deploy on
  timeout with the live colour still serving, then rewrites the upstream snippet and reloads Caddy;
  it refuses to promote when `NativeWeb:ForwardedHeaders:KnownProxies` is unset, or when neither
  `OTEL_EXPORTER_OTLP_ENDPOINT` nor `NativeWeb:Telemetry:Enabled=false` is set; re-running with the
  same SHA is a no-op; unit facts cover idle-colour selection, the migration abort, the readiness
  timeout, both config refusals, and the idempotent re-run.

- [x] **T10 `rollback.sh`** — files: `scripts/deploy/rollback.sh`,
  `tests/nativeweb.web.tests.unit/` — covers: R11 — done when: the script points the upstream
  snippet back at the still-running previous colour and reloads, with no database action; it
  refuses, naming the migration, when the release being rolled back applied one; unit facts cover
  both paths.

## Data durability

- [x] **T11 `backup.sh` and its timer** — files: `scripts/deploy/backup.sh`,
  `scripts/deploy/systemd/nativeweb-backup.service`, `.timer`, an `OnFailure=` alert unit,
  `tests/nativeweb.web.tests.unit/` — covers: R13, R14, R15 — done when: a nightly
  `pg_dump -Fc` is encrypted with `age` and uploaded to the configured S3-compatible endpoint as
  `<host>/<UTC timestamp>.dump.age`; objects beyond the retention period are pruned, and the prune
  is a dry run unless `--execute` is passed; a success heartbeat is sent to the configured URL and
  the script states in its own output when that URL is unset; the timer is `Persistent=true` and a
  failure triggers the alert unit; unit facts cover the retention boundary, the dry-run default and
  the unset-heartbeat warning.

- [x] **T12 `restore.sh`** — files: `scripts/deploy/restore.sh`,
  `tests/nativeweb.web.tests.unit/` — covers: R16, R17 — done when: the script fetches a named or
  latest backup, decrypts it and restores into an explicitly named target; it refuses to target the
  production database without a second flag named for what it does; unit facts cover the
  latest-selection, the decryption path against a scratch database, and the production guard.

## What a machine cannot assert

- [x] **T13 `docs/02-reference/deployment.md` — the runbook** — files:
  `docs/02-reference/deployment.md`, `docs/README.md` — covers: R1, R7, R14, R17, R21 — done when:
  one document carries the provisioning walkthrough, the required-configuration checklist from the
  plan, the deploy and rollback commands, the backup and restore procedures, and the
  disaster-recovery procedure; the `age` key's off-host storage is its first destructive-consequence
  callout; the distribution/version and the reboot decision are recorded with stated defaults; the
  measured times from T15 have named placeholders waiting for them.

- [x] **T14 Verify `MemoryDenyWriteExecute` against a booting instance** — files:
  `scripts/deploy/systemd/nativeweb@.service`, `docs/02-reference/deployment.md` — covers: R3 —
  done when: an instance boots and reaches `/readyz` with the setting on, or the setting is removed
  with the observed failure recorded. Not shipped untested in either direction.
  **Done 2026-08-27, Debian 12 under WSL2.** A `linux-x64` publish booted under the filter and
  answered `/healthz` 200 and `/readyz` 503 (no database), indistinguishable from the same binary
  with it off. The lazy `dlopen` of `libsodium.so` behind Argon2id does not run at startup, so it
  was probed separately under the same filter: `dlopen` succeeded, `sodium_init()` returned 0.
  **Caveat:** a `--user` unit on the WSL2 kernel, not a `--system` unit on the target host. Same
  seccomp filter, different kernel — recorded in the unit file and the runbook.

- [x] **T15 Operator-executed: provisioning walkthrough, restore drill, disaster recovery**
  — **done 2026-09-05.** The restore drill and the disaster-recovery rebuild both ran: backup 1 s,
  restore 1 s, verified serve 1 s, bare guest to serving 345 s, all written into the runbook with
  their caveats. R16 was amended first, because the criterion as written could not fail. Four
  preconditions missing from the DR procedure were found by running it and are now steps 3 to 6 of the procedure.
  **R14 and R17b are not closed by this task and never could have been:** the bucket is a MinIO
  container on the host being backed up, so no copy is offsite. R17 was split on 2026-09-06 for
  that reason, and the drill's result is recorded against R17a.
  — **one third done, 2026-09-01.** `nativeweb-dev` was provisioned and deployed to for real,
  and `https://nw-dev.example.com` serves `apps/sample`: **R1 was met** at that point, with the
  measured release time in the runbook. The run took four attempts and found two defects in the
  deploy scripts, both fixed with tests on `fix/deploy-unit-section`; the runbook's opening note
  names them.
  — files: `docs/02-reference/deployment.md` — covers: R1, R16, R17a — done when: a person has
  followed the provisioning document start to finish on a real host, restored a real backup onto a
  clean database and served a row that existed when the backup was taken while not serving one
  written after it, and rebuilt a serving host from repo plus backup — with the three elapsed times
  written into the runbook as measured values.
  **This task was the user's to run; it could not be completed from a session, and the spec did
  not reach `done` without it.**

- [x] **T16 Records** — files: `work/status/`, `work/tasklist.md`, `work/todo.md`, `CHANGELOG.md`,
  `011-spec.md` — covers: none directly — done when: the status rotation reflects this work, the
  four cutover deploy blockers in `work/todo.md` are updated to say which this spec closed and which
  it did not, the `epass.io`-hostname entry is struck, and `011-spec.md` carries an acceptance review
  stating R15's conditional status and T15's outstanding half.

## Analyze — requirement to task

| R | Tasks | R | Tasks |
|---|---|---|---|
| R1 | T8, T13, T15 | R12 | T5, T9 |
| R2 | T6, T8 | R13 | T11 |
| R3 | T6, T14 | R14 | T11, T13 |
| R4 | T7 | R15 | T11 |
| R5 | T7, T8 | R16 | T12, T15 |
| R6 | T7, T8 | R17a, R17b | T12, T13, T15 |
| R7 | T8, T13 | R18 | T6, T8 |
| R8 | T9 | R19 | **met by shipped code** — `StartupConfigGuards`; T16 records it, no build task |
| R9 | T9 | R20 | T1, T2, T3, T4 |
| R10 | T9 | R21 | T9, T13 |
| R11 | T10 | R22 | T5 |

**No orphan tasks** — every task traces to a "Changes by area" row in `011-plan.md`. **No uncovered
requirement.** R19 is the one requirement with no build task, because `apps/web` already exits `78`
naming the missing value; T16 records that rather than leaving a blank row.

**Two known-conditional outcomes, carried forward rather than hidden:** R15 is met only if the
operator sets a heartbeat URL (T11 makes the script say so when it is unset), and R1/R16/R17a were
only fully met once T15 had been executed by a person -- which it was, on 2026-09-05. R14 and
R17b stay open after it: no task here puts a backup on another machine, because the storage is an
operator decision rather than a change to this repository.
