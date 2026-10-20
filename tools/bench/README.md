# tools/bench — the load harness spec 012 R16 asked for

A reproducible throughput and memory measurement of `apps/sample`, against a committed baseline.
It measured `apps/web` until 2026-08-31, when that app left with the identity provider
(`docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`); the baseline was re-measured on the new
target rather than carried over.
Run it before and after anything that could move the server: a .NET upgrade, a middleware
change, a rewrite of the render path.

```
deno run -A tools/bench/run.ts                  measure, report, compare for information
deno run -A tools/bench/run.ts --check          the same, but a regression exits 1
deno run -A tools/bench/run.ts --baseline       record this measurement as the new baseline
deno run -A tools/bench/run.ts --skip-publish   reuse this harness's existing publish output
```

**Where it publishes: `tmp/bench/publish/`, and only there.** Not the default
`apps/sample/bin/Release/net10.0/<rid>/publish/`, which belongs to the e2e tier's process
fixture — a bench run interleaved with that tier had the two writing one directory, and
`--skip-publish` would benchmark whichever binary the tier had last built and report it as this
harness's own. `--skip-publish` now finds only what this harness published, and says so by name
when it finds nothing.

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
`openssl` was a prerequisite while the harness minted a loopback certificate; it serves over http
now (see below) and `cert.ts` is gone.

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
different hardware, a sandbox that no longer exists. That is the whole reason this harness
exists.

**Small regressions.** Eleven full-render passes across five sessions on one machine and one
unchanged binary ranged 6,887 to 8,012 req/s, 16.3%; peak RSS moved 57.1 MB to 73.8 MB, 29%.
Those were `apps/web` on `GET /`; the spread is a property of the driver and the machine, not of
the app, so it still sizes the tolerances after the retarget.
Back to back within one session the spread is a few per cent; across sessions it is whatever
else the machine was doing. The shipped tolerances are 20% down on throughput and 25% up on
RSS, sized from that, so **this catches gross regressions and not small ones** — a doubling of
page cost, a leak, a middleware that suddenly blocks. Run it on an otherwise idle machine, and
re-record the baseline when the hardware changes.

**A production log volume.** The app is launched at `Logging:LogLevel:Default=Warning`. At
default level a request-logging middleware writes an info line per request and the harness drains
the app's output rather than discarding it, so the run measures its own log draining: on
`apps/web` that took 8,020 req/s down to 6,900, measured. The cost of formatting that line is
therefore not in these numbers and a default-level deployment pays it.

**The production pipeline.** `ASPNETCORE_ENVIRONMENT=Development`, so no HSTS header, and user
secrets layered into configuration on a machine that has any. It was mandatory while the target
was `apps/web`, which refused to start outside Development without its contact, store and
pairwise secrets and an SMTP host; `apps/sample` has no such requirement, and the setting stays
so the measurement keeps meaning the same thing across the retarget.

## Why it is not in CI

A GitHub runner's throughput varies by more than the band above, so every threshold would be
noise and the job would be muted inside a month. `scripts/ci/check-publish-budgets.ps1` is the
CI-side counterpart and measures what a runner can measure honestly: binary size and
time-to-`/healthz`.

## Why http, and why it used to be https

It runs over plain http. `apps/sample` sets no `CookieSecurePolicy.Always` antiforgery cookie, so
a rendered page over http is a 200 and the TLS handshake is measurement noise the harness does
not want.

**This was the reverse until 2026-08-31**, and the reason is worth keeping. `apps/web` could not
serve a rendered page over http: its antiforgery cookie was `__Host-nw-antiforgery` with
`CookieSecurePolicy.Always`, and the layout minted a token on every page, so over http every
render threw and answered 500 — measured, 245,106 of them, all of which the harness happily
recorded as a baseline before it learned to refuse one. So `cert.ts` minted a throwaway CA and
leaf with `openssl`, staged into `tmp/bench/mint/`, and checked on reuse that the leaf still
chained to the CA rather than only that both files existed; an interrupted mint would otherwise
leave a new CA beside the previous leaf and every later run would serve a certificate the
driver's trust anchor never signed, failing as a generic transport error for up to a year.
`cert.ts` left with `apps/web`. Anything pointing this harness at an https target again needs it
back.

No rate-limit override is set: `apps/sample` puts no limiter on its pages, where `apps/web` had
`GET /` behind `RequireRateLimiting("page")` at a limit sized for one human per minute.
`Sample__Telemetry__Enabled=false` is passed, under the section `apps/sample` binds. It used to be
`NativeWeb__Telemetry__Enabled`, which the app does not read: telemetry defaults to off, so the run
was unaffected either way, but the key was measuring nothing. `apps/sample` does register
OpenTelemetry, through `AddNativeWebWeb`.

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
dead sampler would otherwise pass the memory half of every check in silence. Without that second guard a `--baseline` run anywhere else would replace the committed file,
and every later `--check` on the recording machine would exit 0 having compared nothing. That guard exists because the first real run of this harness recorded exactly such a
baseline: 0 req/s and 245,106 errors, written without complaint, under which the floor would
have been unfailable forever.
