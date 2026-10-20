# 002 — Where the OTLP collector lives

Date: 2026-08-24
Status: accepted

## Context

Plan 004 (`docs/plans/2026-08-17-004-enable-telemetry.md`) turned telemetry on by default and
left exactly one thing open: where the collector the app exports to lives. With no
`OTEL_EXPORTER_OTLP_ENDPOINT` set, the exporter defaults to `localhost:4317` and fails in the
background — safe and measured (187 ms shutdown with nothing listening), but it means a default
deployment ships data to nobody.

This repo tracks no deployment configuration at all — the only `deploy/` is the skeleton's — and
plan 004 already rejected shipping a collector inside that plan as "new infrastructure, not
enabling a flag". The question left standing was whether that rejection is the final answer or a
deferral.

## Decision

**This repository ships no collector, by decision rather than omission.** The app's contract is
OTLP/gRPC at whatever endpoint `OTEL_EXPORTER_OTLP_ENDPOINT` names, with the redaction guarantees
documented in `docs/configuration.md`. The collector is deployment infrastructure and lives with
the deployment, whenever one exists.

The reasoning that made plan 004 reject the collector has not expired and is the reasoning here:

- A collector config in this repo would be the repo's first deployment artifact, invented ahead
  of any deployment to hang it on. It would encode a topology nobody has chosen (local agent?
  central collector? vendor backend?) and rot before its first real use.
- Nothing about the app's correctness depends on it. Export failures are background warnings; a
  missing collector costs nothing at runtime.
- The security posture is already complete on the sending side. What crosses the wire is
  documented attribute-by-attribute in `docs/configuration.md`, with regression tests holding
  each guarantee. Where the wire ends is a property of the deployment, not of this code.

For the eventual epass.io deployment, the recommendation (not a requirement — that deployment
decides): an OpenTelemetry Collector on the **same host / trust boundary** as the app, receiving
on `localhost:4317` and forwarding over TLS to wherever the data is finally kept. That is the
shape the exporter's default endpoint already assumes, it keeps the IdP's span traffic — which
includes `url.path`, connection-string host/database, and timing — inside the boundary it was
generated in until it is deliberately shipped out, and it means enabling the deployment is one
`OTEL_EXPORTER_OTLP_ENDPOINT` decision rather than an app change.

## Consequences

- Plan 004's "Still open" item closes as decided, not deferred.
- If a deployment configuration ever lands in this repo, revisiting this decision is part of
  landing it — at that point there is a real topology to configure.
