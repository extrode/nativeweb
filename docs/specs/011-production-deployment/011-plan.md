# 011 — Plan

Spec: ./011-spec.md
Created: 2026-08-27

## Approach

**Blue/green release directories on one host, with Caddy reloading between them.** Two systemd
instances of a template unit (`nativeweb@blue`, `nativeweb@green`) run the same binary from two
release directories on two loopback ports. A deploy stages the new release into the *idle* colour,
applies migrations, starts that colour, polls its own `/readyz` until it answers, and only then
rewrites a one-line Caddy upstream snippet and issues `caddy reload` — which is graceful, so
in-flight requests finish on the old colour and new ones land on the new one. The previous colour
keeps running until the *next* deploy claims it, which makes rollback a Caddy reload rather than a
redeploy.

The alternative considered and rejected was **symlink-flip with a single unit**: stage the release,
point `current` at it, `systemctl restart`. It is materially simpler and it is what most .NET
deployments do, but it cannot meet R10's acceptance criterion — "requests continue to be served by
the previous version **throughout**". A restart has a gap, and if the new binary dies at startup
the gap is unbounded. A verify-then-restart variant (boot the candidate on a scratch port, check
readiness, kill it, restart the real unit) narrows the window to a couple of seconds but does not
close it. For a host that is the sign-in path for every dependent application, closing it is worth
one extra unit instance and a generated snippet file.

**The binary is built in WSL2 and shipped as a tarball.** NativeAOT does not cross-compile Windows
to Linux, so `linux-x64` must be published on Linux. WSL2 was chosen over building on the
production host (which would put an SDK, a clang toolchain and a JauntyQ source checkout on the
identity provider, and have a link step compete with serving traffic) and over the ubuntu CI runner
(blocked today by the unresolved package-mode restore gap, and the manual-deploy decision means CI
holds no production credentials anyway). The build stays in checkout mode — `$(JauntyQRoot)`, the
same mode the CI test job and every local build use — so R22 is met by documenting that package
mode is unavailable rather than by pretending it works.

## Tech / dependencies

Verified against this repo and pinned in the runbook rather than guessed at deploy time.

| Piece | Choice | Why / constraint |
|---|---|---|
| Host OS | Debian stable or Ubuntu LTS, x86-64 | Both carry systemd, nftables and a Postgres the repo's migrations already run against. The runbook names one and states the tested version. |
| Runtime artifact | `dotnet publish apps/web/nativeweb.web.csproj -c Release -r linux-x64` | Self-contained NativeAOT; no runtime install on the host. Existing `PublishAot=true` from `Directory.Build.props`. |
| Build environment | WSL2 (Ubuntu), .NET 10 SDK per `global.json`, `clang` + `zlib1g-dev` for the AOT link | Version read from `global.json` at implementation, not assumed here. |
| TLS + proxy | Caddy 2, from the official apt repository | Automatic ACME issue and renewal (R4), graceful `caddy reload` (R10), `roll_size`/`roll_keep` log rotation (R6). |
| Database | PostgreSQL from the distribution, `listen_addresses = 'localhost'` | Same host by decision; loopback-only binding is what actually satisfies R5, independent of firewall rules. |
| Migrations | `db/migrate.sh`, already in the repo | Per-file transactions, `schema_migrations` tracking, `-- @no-transaction` opt-out. Nothing new is written. |
| Supervision | systemd template unit `nativeweb@.service` | Instance name selects the colour and therefore the port. |
| Backups | `pg_dump -Fc`, encrypted with `age`, uploaded to S3-compatible storage via `aws-cli` (`--endpoint-url`) | Endpoint is configuration, so any S3-compatible provider is interchangeable. `age` gives encryption at rest that does not depend on the provider's promises. |
| Backup schedule | systemd timer, `OnCalendar` nightly with `RandomizedDelaySec` | `Persistent=true` so a host that was off catches up. |
| Backup alerting | `OnFailure=` unit plus a success heartbeat to a configured external URL | The heartbeat is what makes silence detectable (R15); the runbook states plainly that leaving it unset leaves R15 unmet. |
| Firewall | nftables, 22/80/443 inbound only | ufw is a wrapper over the same thing; nftables directly keeps the ruleset readable in the repo. |
| Host patching | `unattended-upgrades`, security pocket only, no automatic reboot | R7 wants a recorded decision; automatic reboots on an unclustered identity host is not one I'd take silently. |

## Changes by area

### New: `scripts/deploy/` — the operator surface

| Path | What it does | Requirements |
|---|---|---|
| `provision.sh` | Bare OS → serving host. Creates the service account, installs Caddy/Postgres/nftables, lays out `/opt/nativeweb` and `/var/lib/nativeweb`, installs the units and timers, writes the nftables ruleset, configures journald retention. Idempotent. | R1, R2, R5, R6, R7 |
| `build.sh` | Run in WSL. Publishes `linux-x64`, stamps a `RELEASE` file with the commit SHA and build time, tars the publish directory to `dist/nativeweb-<sha>.tar.zst`. Refuses to build from a dirty tree unless forced. | R12, R22 |
| `deploy.sh` | Ships the tarball, unpacks into the idle colour, runs `db/migrate.sh`, starts the idle colour, polls its `/readyz`, rewrites `upstream.caddy`, `caddy reload`, records the promotion. Aborts leaving the live colour untouched at any failure. Idempotent — re-running with the same SHA is a no-op after the first success. | R8, R9, R10, R12 |
| `rollback.sh` | Points the upstream snippet back at the still-running previous colour and reloads. Refuses, with the reason named, when the release being rolled back applied a migration. | R11 |
| `backup.sh` | `pg_dump -Fc` → `age` encrypt → upload to the configured S3 endpoint → prune beyond retention → heartbeat on success. | R13, R14, R15 |
| `restore.sh` | Fetch a named or latest backup, decrypt, restore into a target database. Refuses to target the live production database without an explicit second flag. | R16, R17 |
| `systemd/nativeweb@.service` | Hardened template unit: `NoNewPrivileges`, `ProtectSystem=strict`, `ProtectHome=yes`, `PrivateTmp`, empty `CapabilityBoundingSet`, `ReadWritePaths=/var/lib/nativeweb`, `RestrictAddressFamilies`. `Restart=on-failure` with `StartLimitBurst`/`StartLimitIntervalSec` so a persistent failure stops looping. `EnvironmentFile=/etc/nativeweb/secrets.env` (0640 `root:nativeweb`). | R2, R3, R18 |
| `systemd/nativeweb-backup.{service,timer}` | Nightly backup with `Persistent=true` and an `OnFailure=` alert unit. | R13, R15 |
| `caddy/Caddyfile` | Canonical hostname, automatic HTTPS, HTTP→HTTPS redirect, `import upstream.caddy`, bounded access logs. | R4, R6 |
| `nftables/nativeweb.nft` | Inbound 22/80/443, everything else dropped. | R5 |

Scripts are `bash` (they run on Linux and in WSL, and `db/migrate.sh` already sets that precedent).
The global cleanup-script interface — bare run is a dry run, `--execute` acts — applies to
`backup.sh`'s prune and to `restore.sh`, which are the two that destroy things.

### Changed: `apps/web` — R20 only

Nothing else in the application changes. `wwwroot/robots.txt:12` and `wwwroot/sitemap.xml` hardcode
`https://epass.io`, which is wrong on any host that is not epass.io and silently wrong on the one
that is.

- New configuration key **`NativeWeb:CanonicalBaseUrl`** (no existing key covers this — `BaseUrl`
  in `libs/nativeweb.signin.epass` is a *client's* pointer at an IdP, not a server's own identity).
  Absolute `http`/`https`, validated at startup in the same posture as the other validated keys,
  defaulting to the request's own scheme+host when unset so a dev checkout keeps working.
- `robots.txt` and `sitemap.xml` move from static files under `wwwroot/` to endpoints that emit the
  same content with the base URL substituted. Both stay in the `Disallow` list and stay cacheable.
- `docs/02-reference/configuration.md` gains the key. That file asserts it lists every
  `NativeWeb:` key the source reads, so omitting it breaks a documented invariant.

### New: `docs/02-reference/deployment.md` — the runbook

The single document R1 requires: provisioning walkthrough, the required-configuration checklist,
the deploy/rollback commands, the backup and restore procedures with their **measured** times, and
the disaster-recovery procedure. Cross-linked from `docs/README.md` and the `02-reference` index.

### Configuration the runbook must set, and why each matters

Read out of `docs/02-reference/configuration.md` rather than invented:

| Key | Why production must set it |
|---|---|
| `NativeWeb:ForwardedHeaders:KnownProxies` | **The one whose default costs the most.** Behind Caddy, unset collapses every client in the world into one rate-limit partition. Nothing errors. Set to loopback. |
| `NativeWeb:ContactKey`, `StoreEncryptionKey`, `PairwiseSalt` | Absence downgrades security silently; the startup guard names them. |
| `NativeWeb:Smtp:*` | Startup fails outside Development without `Host`; `FromAddress` required alongside it. |
| `NativeWeb:DataProtection:KeyDirectory` | Must point at `/var/lib/nativeweb/keys`, which persists across releases. Default is under the app directory, which a blue/green swap would move. |
| `NativeWeb:WebAuthn:ServerDomain`, `Origins` | Default `localhost` breaks every credential on a real hostname. |
| `NativeWeb:DeviceGrant:VerificationUri` | Default `https://localhost/device` is wrong for any real deployment "and nothing will tell you". |
| `NativeWeb:FirstPartyClientId` | Production points at its own seeded row (migration `0055`). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` **or** `NativeWeb:Telemetry:Enabled=false` | Telemetry is opt-out; unset endpoint means an exporter retrying against nothing. `deploy.sh` refuses to promote when neither is set. | 

## Data / interfaces

- **Filesystem contract.** `/opt/nativeweb/releases/<sha>/` (release trees, root-owned, service-readable),
  `/opt/nativeweb/blue` and `/green` (symlinks into releases), `/etc/nativeweb/secrets.env`,
  `/etc/caddy/upstream.caddy` (generated: one `reverse_proxy 127.0.0.1:<port>` line),
  `/var/lib/nativeweb/keys` (data-protection keys, service-writable and the only writable path).
- **Port contract.** blue = 5011, green = 5012, both loopback-only. Instance name → port is the unit
  file's only piece of logic.
- **Backup object contract.** `s3://<bucket>/<host>/<UTC-date>T<time>Z.dump.age`. Date-sortable so
  "latest" needs no metadata call, and the `.age` suffix makes an unencrypted upload visible.
- **Migration/deploy contract.** Migrations run while the previous release is still serving, so a
  migration must be backward-compatible with the release it is deployed alongside (expand now,
  contract in a later release). `db/migrate.sh` holds locks for a whole file — already documented in
  the script and in `docs/02-reference/migration-lock-review.md` — so long migrations against a live
  database must be split. The runbook restates this at the point of use.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| **Migrations run against a live previous release.** A contracting migration (drop/rename) breaks the running version between `migrate` and `reload`. | Documented expand/contract rule; `rollback.sh` refuses for schema-changing releases and says so; the runbook states the split-long-migrations rule with the lock behaviour behind it. |
| **`KnownProxies` unset defeats every rate limit, silently.** This is an auth surface — the sign-in, register and OAuth limiters all key on client IP. | Required-configuration checklist, and `deploy.sh` fails the promotion if it is unset. A check the deploy enforces beats a line in a document. |
| **Secrets in `secrets.env` readable by the wrong account.** | Mode 0640 `root:nativeweb`, `provision.sh` asserts the mode on every run, and the unit uses `EnvironmentFile` rather than `Environment=` so values never appear in `systemctl show`. |
| **The blue/green pair share one database and one data-protection key directory.** Two versions with incompatible key handling would fail authentication for live sessions. | Both colours point at the same persistent `KeyDirectory`, which is the current behaviour with a shared directory rather than a new mechanism. Verified in the deploy's readiness gate, which authenticates. |
| **`age` key loss makes every backup unreadable.** The most likely way this whole plan fails in practice. | The key is generated during provisioning, and the runbook's first destructive-consequence callout is that it must be stored somewhere that is not this host and not the backup bucket. The restore drill (R16) proves the stored copy works. |
| **A restore drill against production.** | `restore.sh` requires an explicit target and a second flag before touching a database that is not obviously scratch. |
| **NativeAOT under `MemoryDenyWriteExecute`.** No JIT, so it should hold — but "should" is not measured. | Enabled in the unit and **verified by a booting instance during implementation**; dropped with a recorded reason if it fails, rather than shipped untested either way. |
| **Package-mode restore still unresolved.** | Out of scope by R22's wording: the build mode used is checkout mode, documented, and known to work. The gap stays on `work/todo.md`. |
| **Heartbeat URL unset ⇒ R15 unmet.** | Stated as an unmet requirement in the runbook rather than quietly skipped, and the acceptance review records it. |

## Testing

Every layer, per the project's testing rule. `tests/nativeweb.web.tests.unit/MigrateRunnerTests.cs`
is the precedent: shell behaviour is driven from xUnit against scratch state, not asserted by
reading the script.

- **Unit** — new xUnit facts driving each script's decision logic against scratch directories and
  scratch databases: idle-colour selection, dirty-tree refusal, idempotent re-deploy, the
  readiness-poll timeout path, rollback's migration refusal, backup pruning boundaries, `restore.sh`'s
  production guard, the required-configuration check. Plus C# unit coverage of the
  `CanonicalBaseUrl` validation and the robots/sitemap rendering.
- **Integration** — `WebApplicationFactory` against real PostgreSQL for the two new endpoints,
  asserting the emitted base URL follows configuration and that both stay cacheable and disallowed.
- **E2E** — Playwright fetches `/robots.txt` and `/sitemap.xml` from a running app and asserts no
  `epass.io` literal survives when a different canonical URL is configured.
- **Not automatable, and stated as such**: the provisioning walk-through (R1), the restore drill
  (R16) and the disaster-recovery run (R17) are executed by a human once and their measured times
  written into the runbook. A test asserting a script "would" provision a host is a test asserting
  nothing.

## Coverage check

| R | Covered by | R | Covered by |
|---|---|---|---|
| R1 | `provision.sh` + runbook | R12 | `build.sh` RELEASE stamp, `/version`, `deploy.sh` record |
| R2 | service account, unit hardening | R13 | `backup.sh` + timer |
| R3 | `Restart=on-failure` + start-limit | R14 | `age` + S3 upload + prune |
| R4 | Caddy automatic HTTPS | R15 | `OnFailure=` unit + success heartbeat |
| R5 | nftables + Postgres loopback binding | R16 | `restore.sh` + drill, time recorded |
| R6 | journald retention + Caddy log rolling | R17 | DR procedure in the runbook |
| R7 | `unattended-upgrades`, recorded decision | R18 | `secrets.env` 0640, `EnvironmentFile` |
| R8 | `deploy.sh`, idempotent | R19 | **already met** — `StartupConfigGuards` exits `78` (EX_CONFIG) naming the value; verified, not rebuilt |
| R9 | `db/migrate.sh` before promotion, abort on failure | R20 | `NativeWeb:CanonicalBaseUrl` + dynamic robots/sitemap |
| R10 | blue/green + readiness poll + `caddy reload` | R21 | `deploy.sh` refusal when neither endpoint nor disable is set |
| R11 | `rollback.sh` (Caddy reload only) | R22 | checkout-mode build, documented |

**No requirement is uncovered.** Two are met by existing code rather than new work — R19 by
`StartupConfigGuards`, and R10's readiness signal by the `/readyz` and `ReadinessCache` that
`apps/web` already ships. One, R15, is met only if the operator supplies a heartbeat URL, and the
runbook says so in those words rather than implying otherwise.

## Needs decision

None blocking. Two choices are deliberately deferred to the runbook, where they are recorded as
decisions with stated defaults rather than left implicit: the exact distribution and version, and
whether `unattended-upgrades` may reboot.
