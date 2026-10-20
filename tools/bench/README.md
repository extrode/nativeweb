# tools/bench: the load harness

A reproducible throughput and memory measurement of `apps/sample`, against a committed baseline.
Run it before and after anything that could move the server: a .NET upgrade, a middleware
change, a rewrite of the render path.

```bash
deno run -A tools/bench/run.ts                  measure, report, compare for information
deno run -A tools/bench/run.ts --check          the same, but a regression exits 1
deno run -A tools/bench/run.ts --baseline       record this measurement as the new baseline
deno run -A tools/bench/run.ts --skip-publish   reuse this harness's existing publish output
```

**Where it publishes: `tmp/bench/publish/`, and only there.** Not the default
`apps/sample/bin/Release/net10.0/<rid>/publish/`, which belongs to the e2e tier's process
fixture. Sharing it would let `--skip-publish` benchmark whichever binary the tier last built.
`--skip-publish` finds only what this harness published, and says so by name when it finds
nothing.

Other flags: `--duration <seconds>`, `--runs <n>`, `--concurrency <n>`, `--port <n>`, and
`--force` (with `--baseline`, to replace a baseline recorded on a different machine). Changing
any of the three harness settings makes the run non-comparable to the baseline, and the tool
says so rather than comparing anyway.

**Exit codes**, which is where a harness fails quietly if it fails at all:

| code | meaning |
|---|---|
| 0 | within tolerance — or, without `--check`, anything at all |
| 1 | `--check` found a regression, named per scenario and metric |
| 2 | `--check` compared **nothing**: no baseline yet, or harness settings that differ from the baseline's |

Exit 2 is the one to notice. `--check --duration 5` against a ten-second baseline is not a pass
and does not get to look like one; a *machine* mismatch is different and stays record-only at
exit 0, because nothing the operator types changes which machine they are on.

## Prerequisites

For the publish step only, a restorable .NET SDK; JauntyQ comes from its nuget.org packages.
`--skip-publish` against an existing `tmp/bench/publish/` needs neither dotnet nor JauntyQ.

## What it measures

| scenario | path | why |
|---|---|---|
| `healthz` | `/healthz` | the floor: routed, renders nothing, touches no database |
| `articles` | `/articles` | the full page render — template, antiforgery mint, a list of rows |

`/articles`, not `/`: on `apps/sample` the root is a 302 to `/admin`, so a scenario pointed there
would measure the router and report a redirect as a successful render. `run.test.ts` asserts no
scenario can be `/`.

Per scenario: one discarded warm-up pass, then three timed passes averaged, section 14's own
"three runs per test" discipline. Peak RSS is sampled four times a second while load runs.
Every run also writes `tmp/bench/latest.json`, which is gitignored.

## What it does not measure

**Absolute performance.** The driver is Deno's `fetch`, not `wrk`, because neither `wrk` nor
`bombardier` nor `hey` is installed here and the repo's tooling rule is Deno over anything
needing npm. Real JS time is spent per request, so the numbers are a floor for the server, not
its ceiling. **They are not comparable to the figures in
`docs/01-architecture/native-web-architecture.md` §14** — different apps, different tool,
different hardware. That is the whole reason this harness exists.

**Small regressions.** Run to run, the spread on one machine and one unchanged binary is a
property of the driver and the machine, not of the app, and it sizes the tolerances. Back to back within one session the spread is a few per cent; across sessions it is whatever
else the machine was doing. The shipped tolerances are 20% down on throughput and 25% up on
RSS, sized from that, so **this catches gross regressions and not small ones** — a doubling of
page cost, a leak, a middleware that suddenly blocks. Run it on an otherwise idle machine, and
re-record the baseline when the hardware changes.

**A production log volume.** The app is launched at `Logging:LogLevel:Default=Warning`. At
default level a request-logging middleware writes an info line per request and the harness drains
the app's output rather than discarding it, so the run would measure its own log draining. The cost of formatting that line is
therefore not in these numbers and a default-level deployment pays it.

**The production pipeline.** `ASPNETCORE_ENVIRONMENT=Development`, so no HSTS header, and user
secrets layered into configuration on a machine that has any. `apps/sample` has no startup
secrets requirement, and the setting stays so the measurement means the same thing from run to run.

## Why it is not in CI

A GitHub runner's throughput varies by more than the band above, so every threshold would be
noise and the job would be muted inside a month. `scripts/ci/check-publish-budgets.ps1` is the
CI-side counterpart and measures what a runner can measure honestly: binary size and
time-to-`/healthz`.

## Why http

It runs over plain http. `apps/sample` sets no `CookieSecurePolicy.Always` antiforgery cookie, so
a rendered page over http is a 200 and the TLS handshake is measurement noise the harness does
not want. An app whose antiforgery cookie requires https answers every rendered page with a 500
over http, so pointing this harness at one needs an https target and a trusted certificate.

No rate-limit override is set: `apps/sample` puts no limiter on its pages.
`Sample__Telemetry__Enabled=false` is passed, under the section `apps/sample` binds. Telemetry
defaults to off; `apps/sample` does register OpenTelemetry, through `AddNativeWebWeb`.

## baseline.json

Carries the measurement, the harness settings, and a machine fingerprint (hostname, core
count, OS, arch). A run on another machine prints its numbers and says there is nothing to
compare against; a run with different settings does the same, and under `--check` exits 2 (see
the exit-code table above).

The file's own numbers are validated on read, not only its envelope: a hand-edited
`"rps": "6998"` is valid JSON, and left unchecked it would make `base.rps * 0.80` NaN and every
later comparison unfailable while the file still looked like a baseline.

`--baseline` refuses to write a measurement with failed requests, zero throughput, or no RSS
reading, and refuses to replace a baseline recorded on a different machine unless `--force` says
to. A **measured** zero RSS is refused on the read side too: `0 > baseline × 1.25` is false, so a
dead sampler would otherwise pass the memory half of every check in silence. The machine guard
stops a `--baseline` run elsewhere from replacing the committed file, which would leave every later
`--check` on the recording machine comparing nothing.
