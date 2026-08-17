# 004 — Enable telemetry

Standard tier. Completes spec 003 R33, whose hooks shipped off by default with an in-code
reminder to enable them before the epass.io traffic switch. The user made that call on
2026-08-17: enable it.

## Approach

Flip the gate from opt-in to opt-out and give the emitted data a service identity. Two changes,
for two different reasons.

**Opt-out.** `NativeWeb:Telemetry:Enabled` is compared against `"true"` today, so telemetry is on
only where something remembers to set it. That is the shape that produces the failure the user
wanted to avoid — a flag first exercised in production during the cutover. Comparing against
`"false"` instead means the deployed binary exports unless an operator deliberately silences it,
and the off-switch stays available for a test host or a developer with no collector.

**Service identity.** Measured on `dev` before any change (probe run, 2026-08-17): the resource
carries `service.name = unknown_service:testhost` and nothing else beyond the three
`telemetry.sdk.*` attributes. Two instances of this app, or this app beside any other, are
indistinguishable at the collector. That makes the data unusable rather than merely untidy, so
`AddService` with the assembly's informational version is part of enabling, not a follow-up.

### What measurement ruled out

The concern raised when this was proposed was that span attributes on an IdP might carry
credentials or subject identifiers. Probed rather than assumed, and largely they do not:

- **`url.query` values are already redacted.** A request to
  `/healthz?returnUrl=%2Fsettings&state=abc123&code=SECRET` emits
  `url.query = ?returnUrl=Redacted&state=Redacted&code=Redacted`. Parameter *names* survive,
  values do not. OpenTelemetry.Instrumentation.AspNetCore 1.17.0 does this by default.
- **`url.path` is raw, but no route carries a secret.** Full inventory of `Map(Get|Post)` across
  `apps/web`: every template is a fixed path except `{id:long}`, `{clientId:guid}` and `{id}`.
  There is no `/u/{username}`-shaped route, and no login-link or reset token appears in a path —
  those endpoints (`/auth/passphrase/reset/verify`, `/oauth/login-link/start`) take their token
  in a query parameter, whose value is redacted, or in a POST body, which is not captured at all.

### What review changed

The first cut concluded from the above that no app-level redaction was needed. Review pushed back
on two points and was right on both.

**Npgsql was never probed.** Only ASP.NET Core spans had been. This app puts passphrase hashes,
session tokens and encrypted contact data through Npgsql, and the driver's `ActivitySource` and
`Meter` are subscribed by name — so it was the largest unexamined surface, not a detail. Probed:

- `db.npgsql.data_source` and `db.client.connection.pool.name` are the connection string. The
  driver **strips the password** — verified with one present, since the dev string has none and
  the first probe therefore proved nothing. Host, database and DB username do leave the process.
- `db.query.text` is the parameterised SQL. A failing `SELECT @v::int` reports `SELECT @v::int`,
  never the value.
- A failed query reports **SQLSTATE only** — `22P02` in the status description, `error.type` and
  `db.response.status_code`. The Postgres error text, which quotes the offending value back, does
  not reach the span.

No leak, but four package defaults now standing between an IdP and a collector, so
`NpgsqlTelemetryTests` holds each as a regression test.

**Query redaction is switchable from outside the app.** The package reads
`OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` (confirmed present in the 1.17.0
assembly) and the matching option is `internal`, so a host can turn redaction off and the app has
no in-code way to refuse. For an identity provider, "an OAuth code never leaves this process" is
worth holding somewhere an environment variable cannot reach past, so `QueryRedactingProcessor`
re-redacts before export. Names are kept rather than dropping the attribute, because on
`/oauth/authorize` whether the response carried `code=` or `error=` is the first thing anyone
debugging a client integration wants, and a name says nothing a route template does not.

That processor is load-bearing rather than belt-and-braces, and the perturbation shows it: with
the host disabling the package's redaction, removing the processor turns the test red.

Two further review points were measured and did **not** reproduce:

- *Shutdown hang with no collector.* 187 ms with nothing listening, 201 ms with a blackholed
  endpoint. No explicit exporter timeout needed.
- *`AddService` swallowing `OTEL_SERVICE_NAME`.* Real, and fixed — the variable is honoured.

### Alternatives rejected

- **Set it on in `appsettings.json`.** Reaches all 264 integration tests, giving every test host
  an exporter background loop and OTLP retries against a collector that is not there. The code
  default plus an env-var off-switch in the test factory keeps the tier quiet.
- **Ship a collector.** This repo tracks no deployment configuration at all — the only `deploy/`
  is the skeleton's. Inventing one here would be new infrastructure, not enabling a flag. The
  collector's address is an `OTEL_EXPORTER_OTLP_ENDPOINT` the operator sets; where it points is
  the one thing still open.
- **Change the skeleton to match.** The skeleton is the reference template. Default-off is right
  for something someone else stamps a product out of; default-on is a deployment decision this
  product has made and a template should not presume. The two deliberately diverge.

## Changes by area

- `apps/web/Program.cs` — gate compares against `"false"`; `ConfigureResource` adds
  `service.name` (overridable by `OTEL_SERVICE_NAME`) and `service.version`. The informational
  version is read once at builder time and reused by `/version`, which computed its own copy.
- `apps/web/QueryRedactingProcessor.cs` — re-redacts `url.query` before export.
- `tests/nativeweb.web.tests.e2e/AppProcessFixture.cs` — telemetry off. This tier drives
  `/dev/inbox/{destination}`, so without it an e2e run would export email addresses to anything
  listening on `localhost:4317`.
- `tests/nativeweb.web.tests.integration/NativeWebFactory.cs` — `NativeWeb__Telemetry__Enabled=false`
  in the static ctor, beside the other tier-wide env overrides. Safe because `UseSetting` outranks
  a process environment variable under `CreateSlimBuilder` — measured, not assumed, so the
  telemetry tests can still turn it back on.
- `tests/nativeweb.web.tests.integration/TelemetryTests.cs` — extend from the single boot smoke
  test to cover the resource attributes, the query redaction, the meters, and the off-switch.
- `docs/configuration.md`, `docs/security-baseline.md` — the key's default inverts; the
  known-open item closes.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| A package bump stops redacting `url.query` | A test asserts the redaction on a URL carrying `code=` and `state=` |
| Export failure takes down a request | Already established: exporter failures are background warnings. `TelemetryTests` boots with it on and serves; CI's publish-budget step launches the NativeAOT image with it on |
| An operator wants it off | `NativeWeb:Telemetry:Enabled=false`, documented |
| NativeAOT trim breakage from `ConfigureResource` | `TreatWarningsAsErrors` catches the analyzer warning at build; the publish-budget step exercises the native image |

## Coverage check

R33 ("wired but disabled by default; enabling is a configuration change") is superseded here by
the user's decision. The acceptance criterion in 003-spec.md:183 — "Given `Telemetry:Enabled=true`
and a local collector, request percentiles, runtime metrics ..." — is unchanged in substance; only
the default it is reached from moves.

## Still open

Where the collector lives. Not blocking: with no `OTEL_EXPORTER_OTLP_ENDPOINT` set, the exporter
defaults to `localhost:4317` and fails in the background, which is exactly the pre-change state
minus the flag.
