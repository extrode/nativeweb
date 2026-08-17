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

So no custom `Filter` or `EnrichWithHttpRequest` redaction is needed. The redaction behaviour is
a default, though, and a default can change under a package bump — hence a test that asserts it
rather than a paragraph that assumes it.

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
  `service.name` and `service.version`. The informational version is read once at builder time
  and reused by `/version`, which computed its own copy.
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
