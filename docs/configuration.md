# Configuration reference — apps/web

Every setting `apps/web` reads, what it defaults to, and what a wrong or absent value actually
costs. Until this file existed, all of it was discoverable only by reading `Program.cs`.

The skeleton (`skeleton/`) reads a smaller subset under its own `__PRODUCT__:` prefix; the shape
and the defaults are the same, and the section names line up one for one.

## How settings are supplied

| Source | When | Notes |
|---|---|---|
| `appsettings.json` | always | Committed. Holds only `Logging` and the dev-seed `FirstPartyClientId` — never a secret |
| User secrets | Development only | `CreateSlimBuilder` does not load them; `Program.cs` adds them explicitly when `IsDevelopment()` |
| Environment variables | always | The deployment path |

**Environment-variable spelling replaces `:` with `__`.** `NativeWeb:Db:MaxPoolSize` is
`NativeWeb__Db__MaxPoolSize`. The connection string is `ConnectionStrings__NativeWeb`.

**Pre-rename keys are fatal, deliberately.** N4 renamed every key from `Epass:*` to `NativeWeb:*`.
An environment still exporting `Epass__ContactKey` or `ConnectionStrings__Epass` would otherwise
start up looking healthy while writing contact PII in plaintext, so startup refuses instead. Loud
exactly once, on the deploy that carries the rename — the only moment it can be wrong.

## Required outside Development

`Program.cs` refuses to start when any of these is missing and the environment is not Development.
The check is deliberate: each one degrades silently rather than crashing, so a lenient deploy looks
healthy while being materially less safe than it appears.

| Key | Absent means |
|---|---|
| `NativeWeb:ContactKey` | Contact PII lands in the clear. `ContactProtector` reads legacy plaintext back transparently, so nothing downstream complains |
| `NativeWeb:StoreEncryptionKey` | The staged registration passphrase sits unencrypted in `bff_store` while the user verifies their code |
| `NativeWeb:PairwiseSalt` | `/oauth/introspect` returns 503 |
| `NativeWeb:Smtp:Host` | Refuses to start rather than fall back to the dev senders, which write verification codes and login links — both account-takeover-grade bearer credentials — to the log |

In Development all four are optional, so a fresh checkout runs before any secrets exist. The
affected endpoints return 503 instead.

## Database

`ConnectionStrings:NativeWeb` is the connection string. **A missing one is not fatal**, even
outside Development: no data source is registered, `/readyz` reports not-ready, and the app still
serves. That is a deliberate skeleton-inherited posture, not an oversight.

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:Db:MinPoolSize` | `1` | Values `>= 0` accepted; anything else falls back to the default |
| `NativeWeb:Db:MaxPoolSize` | `50` | Must be `> 0` |
| `NativeWeb:Db:TimeoutSeconds` | Npgsql's own (15) | Only applied when `> 0`; otherwise Npgsql's default stands |
| `NativeWeb:Db:CommandTimeoutSeconds` | Npgsql's own (30) | Same |
| `NativeWeb:Db:MaxAutoPrepare` | `20` | **Set to `0` behind a transaction-pooling PgBouncer.** Prepared statements break mid-flight there, and the remediation must not require a code change |

Every one of these parses leniently: a malformed value is silently replaced by the default rather
than rejected. That is worth knowing when a tuning change appears to have had no effect.

## Rate limiting

Five named per-IP fixed-window policies, all on a **60-second window** with `QueueLimit = 0` and a
429 rejection status. Only the permit count is configurable.

| Key | Default | Surface |
|---|---|---|
| `NativeWeb:RateLimit:Auth:PermitLimit` | `20` | Sign-in, register, passphrase and the rest of the human auth surface |
| `NativeWeb:RateLimit:Admin:PermitLimit` | `20` | Operator-only, already gated by the Operator claim |
| `NativeWeb:RateLimit:OAuthAuthorize:PermitLimit` | `30` | `GET /oauth/authorize` and both consent POSTs. Higher because retries and multiple app launches are normal |
| `NativeWeb:RateLimit:OAuth:PermitLimit` | `120` | `/oauth/token`, `/oauth/introspect`, `/oauth/device_authorization`, `/oauth/login-link/start`. Highest because a native client legitimately polls during the device flow |
| `NativeWeb:RateLimit:CspReport:PermitLimit` | `10` | `POST /csp-report`, which only feeds the log |
| `NativeWeb:RateLimit:Page:PermitLimit` | `240` | Unauthenticated page-rendering GETs (`/`, the sign-in/register/2FA/WebAuthn/reset pages, `security.txt`). Deliberately loose: a flood ceiling, not a security control, since each mints an antiforgery token per request. Probes (`/healthz`, `/version`, `/readyz`) are excluded — a 429 there reads as an unhealthy instance |

These are configurable mainly because `WebApplicationFactory`'s `TestServer` has no real socket, so
every request in a test process partitions under one `"unknown"` key and a large suite would
otherwise exceed a production-appropriate default within a single run.

**Partitioning:** by client IP, with IPv6 bucketed to its `/64` prefix. A v6 client routinely holds
a whole `/64`, so keying on the full address would hand one attacker 2^64 fresh windows. IPv4 and
v4-mapped v6 stay per-address.

### `NativeWeb:ForwardedHeaders:KnownProxies` — the one whose default costs the most

Unset. Comma-separated proxy/load-balancer addresses; unparseable entries are skipped silently.

ASP.NET Core trusts only loopback by default, so behind a reverse proxy `RemoteIpAddress` is the
proxy's own socket peer and **every client in the world collapses into a single rate-limit
partition**. Nothing errors, no log line says so, and the limits above stop being per-client. The
default is the safe direction — an untrusted hop cannot spoof its way past a limit — but it is
only correct for a deployment with no proxy in front of it. Production must set this.

## Readiness

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:Readiness:CacheSeconds` | `1.0` | **Clamped to a maximum of 30**, not merely validated as positive |

`/readyz` is bounded by this cache rather than by a rate limit, because a 429 on a readiness probe
is an off-switch for a healthy instance: partitioning is per-IP, and until `KnownProxies` is
configured the load balancer's own probe shares a partition with every client. The clamp exists
because readiness is what a balancer uses to notice an outage — a fat-fingered value must not let
the app claim health for minutes after its database has gone.

## Telemetry

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:Telemetry:Enabled` | unset (**on**) | Compared case-insensitively against the literal `"false"`; anything else — including unset — is on |

Metrics and traces over OTLP, including Npgsql's built-in `ActivitySource` and `Meter`. The
endpoint and protocol come from the standard `OTEL_EXPORTER_OTLP_*` environment variables, not from
a `NativeWeb:` key. The default endpoint is `localhost:4317`; with no collector there, export
failures are background warnings and never request failures.

**On by default since 2026-08-17.** It was opt-in until then, which is the shape that produces a
flag first exercised in production. `"0"`, `"no"` and a padded `" false "` are *not* off — the
comparison fails toward exporting, because the failure being guarded against is a host that
quietly stopped reporting and was noticed only when someone went looking for a graph.

Resource attributes are `service.name = nativeweb.web` and `service.version = <informational
version>`, the same string `/version` returns. Without them a collector sees
`unknown_service:<host process>` and cannot tell two instances apart. `OTEL_SERVICE_NAME`
overrides the name — one binary running staging and production wants two names, and a default
that silently swallowed the standard variable would be worse than no default.

### What leaves the process

Measured 2026-08-17, not inferred from the semantic conventions. Every row is a package default,
so each has a regression test; a driver or instrumentation bump that changes one fails the build
rather than quietly changing what a collector receives.

| Attribute | Carries | Test |
|---|---|---|
| `url.query` | Only the **names** of parameters that had a value; the value is replaced with `Redacted`, and a segment with no `=` at all is replaced whole | `TelemetryTests`, `QueryRedactingProcessorTests` |
| `url.path` | The raw path. No production route has a secret in one — the only route parameters are `{id:long}`, `{clientId:guid}`, `{id}` | — |
| `db.query.text` | The **parameterised** SQL. Values travel as parameters and never appear | `NpgsqlTelemetryTests` |
| `db.npgsql.data_source`, `db.client.connection.pool.name` | The connection string **with the password stripped by the driver**. Host, database and DB username do leave the process | `NpgsqlTelemetryTests` |
| span status on a failed query | SQLSTATE only (e.g. `22P02`). The Postgres error *text*, which quotes the offending value back, does not reach the span | `NpgsqlTelemetryTests` |

The query redaction is **not** left to the instrumentation. The package redacts by default, but it
reads `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` and the matching option is
`internal`, so a host could switch it off and the app would have no in-code way to refuse. This is
an identity provider — OAuth codes, device codes and passphrase-reset tokens all travel as query
parameters — so `QueryRedactingProcessor` re-redacts before export regardless.

One PII path exists and it is Development-only: `/dev/inbox/{destination}` takes an email address
in the path, so a developer host would send those addresses as `url.path`. The route is mapped
only under `IsDevelopment()` and never exists in a production posture, and both e2e launchers
(`AppProcessFixture` and `StartupGuardProcessTests`) turn telemetry off — but point a dev host at
a throwaway collector, not the real one.

### Running without a collector

Safe, and measured rather than assumed: shutdown takes **187 ms** with nothing listening on the
endpoint and **201 ms** with a blackholed one, so a missing collector costs no shutdown latency
worth bounding. The batch processor's queue is bounded and drops on overflow, and OTel routes
export failures through its own `EventSource` rather than `ILogger`, so there is no log spam.

## Antiforgery and data protection

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:DataProtection:KeyDirectory` | `<AppContext.BaseDirectory>/keys` | Point at a persistent volume in production |

Without a persisted key ring, every restart invalidates outstanding antiforgery tokens, so the
first form submission after any deploy or recycle fails. The default survives ordinary restarts
because `bin/` is not touched; it does not survive a container replacement. The session cookie is
unaffected — it is an opaque random token validated against PostgreSQL, with no protect/unprotect
step.

## Bot traps

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:BotTraps:MinAgeSeconds` | `3` | **Validated, not lenient**: a malformed or negative value throws at startup |

This is the one numeric key that fails fast rather than falling back, because a silently-ignored
value would disable the timing trap while looking configured. The test tiers set it to `0`, their
submits being machine-fast by construction.

## OAuth

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:FirstPartyClientId` | `00000000-0000-4000-8000-0000000e9a55` | A public identifier, not a secret, so a dev-seed default ships in `appsettings.json`. Production points at its own seeded row. **Parsed with `Guid.Parse`, so a malformed value throws at startup** |
| `NativeWeb:DeviceGrant:TtlSeconds` | `600` | RFC 8628 device grant |
| `NativeWeb:DeviceGrant:IntervalSeconds` | `5` | Poll interval advertised to clients |
| `NativeWeb:DeviceGrant:UserCodeLength` | `8` | |
| `NativeWeb:DeviceGrant:VerificationUri` | `https://localhost/device` | Emitted to clients today, but the page itself is a deferred slice — so the default is wrong for any real deployment and nothing will tell you |

## WebAuthn

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:WebAuthn:ServerDomain` | `localhost` | The RP ID. Must equal, or be a registrable-domain suffix of, every configured origin's host |
| `NativeWeb:WebAuthn:Origins` | `http://localhost:53185` | Comma-separated exact origins the browser may present in `clientDataJSON` |

The dev defaults target `launchSettings.json` over plain HTTP: WebAuthn's secure-context rule
treats `localhost` as an exception, so this works locally without TLS. A production mismatch fails
every attestation and assertion at verification time — loudly, not silently.

## Mail

Binds the whole `NativeWeb:Smtp` section to `SmtpOptions`.

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:Smtp:Host` | unset | **Unset means SMTP is disabled.** In Development that selects the log-based dev senders; outside it, startup fails |
| `NativeWeb:Smtp:Port` | `587` | Submission over STARTTLS |
| `NativeWeb:Smtp:Username` | unset | Empty skips SMTP AUTH |
| `NativeWeb:Smtp:Secret` | unset | The AUTH password. Named `Secret` rather than the obvious word so the repo's secret scanner does not false-positive on wiring code |
| `NativeWeb:Smtp:FromAddress` | unset | **Required whenever `Host` is set**; validation throws otherwise |
| `NativeWeb:Smtp:FromName` | unset | Display name only |
| `NativeWeb:Smtp:SecurityMode` | `StartTls` | `None` is test-only and is **rejected for any non-loopback host**, so a production typo cannot send codes, login links and AUTH credentials in cleartext |
| `NativeWeb:Smtp:TimeoutSeconds` | `10` | Connect and send |

## Breach checking

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:BreachCheck:Hibp` | enabled | Opt-out only: the literal `"false"` (case-insensitive) drops to the offline common-password list alone |
| `NativeWeb:BreachCheck:RecheckAfterHours` | `24` | While `last_breach_check_at` is younger than this, sign-in skips the check. Must be `> 0` |

Turn the HIBP leg off for a deployment with no egress, or a test tier that must not depend on a
third party's corpus. Sign-in checks the passphrase it was just given, so the checker's 2s timeout
otherwise sits on every login rather than only on registration.

## Passphrase reset

| Key | Default | Notes |
|---|---|---|
| `NativeWeb:PassphraseReset:MaxCodesPerDay` | `5` | Per-account cap on reset codes. Must be `> 0` |

## Validation posture, summarised

Worth knowing which keys will tell you they are wrong and which will not.

| Behaviour | Keys |
|---|---|
| **Throws at startup** | `BotTraps:MinAgeSeconds`, `FirstPartyClientId`, `Smtp:FromAddress` and `Smtp:SecurityMode` (when `Host` is set), any pre-rename `Epass:*` key, and the four required secrets outside Development |
| **Clamped** | `Readiness:CacheSeconds` (max 30) |
| **Silently defaulted** | every other numeric key — `Db:*`, all six `RateLimit:*`, `DeviceGrant:*`, `BreachCheck:RecheckAfterHours`, `PassphraseReset:MaxCodesPerDay` |
| **Silently ignored** | unparseable entries in `ForwardedHeaders:KnownProxies` and `WebAuthn:Origins` |

`tests/nativeweb.web.tests.unit/ConfigurationReferenceTests.cs` asserts this file lists every
`NativeWeb:` key the source actually reads, and lists no key it does not — so a new setting cannot
land undocumented.
