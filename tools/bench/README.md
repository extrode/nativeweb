# tools/bench — the load harness spec 012 R16 asked for

A reproducible throughput and memory measurement of `apps/web`, against a committed baseline.
Run it before and after anything that could move the server: a .NET upgrade, a middleware
change, a rewrite of the render path.

```
deno run -A tools/bench/run.ts                  measure, report, compare for information
deno run -A tools/bench/run.ts --check          the same, but a regression exits 1
deno run -A tools/bench/run.ts --baseline       record this measurement as the new baseline
deno run -A tools/bench/run.ts --skip-publish   reuse this harness's existing publish output
```

**Where it publishes: `tmp/bench/publish/`, and only there.** Not the default
`apps/web/bin/Release/net10.0/<rid>/publish/`, which belongs to
`tests/nativeweb.web.tests.e2e/AppProcessFixture.cs` — a bench run interleaved with the e2e
tier had the two writing one directory, and `--skip-publish` would benchmark whichever binary
that tier had last built and report it as this harness's own. `--skip-publish` now finds only
what this harness published, and says so by name when it finds nothing.

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

`openssl` (for the loopback certificate; Git for Windows ships one and the resolver looks
there), and — for the publish step only — the JauntyQ sibling checkout that
`Directory.Build.props` requires. `--skip-publish` against an existing `tmp/bench/publish/`
needs neither dotnet nor JauntyQ.

## What it measures

| scenario | path | why |
|---|---|---|
| `healthz` | `/healthz` | the floor: routed, renders nothing, touches no database |
| `home` | `/` | the full page render — Layout, CSP nonce, antiforgery mint |

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

**Small regressions.** Eleven `home` passes across five sessions on one machine and one
unchanged binary ranged 6,887 to 8,012 req/s, 16.3%; peak RSS moved 57.1 MB to 73.8 MB, 29%.
Back to back within one session the spread is a few per cent; across sessions it is whatever
else the machine was doing. The shipped tolerances are 20% down on throughput and 25% up on
RSS, sized from that, so **this catches gross regressions and not small ones** — a doubling of
page cost, a leak, a middleware that suddenly blocks. Run it on an otherwise idle machine, and
re-record the baseline when the hardware changes.

**A production log volume.** The app is launched at `Logging:LogLevel:Default=Warning`. At
default level `RequestLoggingMiddleware` writes an info line per request and the harness drains
the app's output rather than discarding it, so the run measures its own log draining: 8,020
req/s on `home` became 6,900, measured. The cost of formatting that line is therefore not in
these numbers and a default-level deployment pays it.

**The production pipeline.** `ASPNETCORE_ENVIRONMENT=Development`, and it has to be — outside
Development the app refuses to start without the contact, store and pairwise secrets and an
SMTP host. So no HSTS header, the `/dev/inbox` route present, and user secrets layered into
configuration on a machine that has any.

## Why it is not in CI

A GitHub runner's throughput varies by more than the band above, so every threshold would be
noise and the job would be muted inside a month. `scripts/ci/check-publish-budgets.ps1` is the
CI-side counterpart and measures what a runner can measure honestly: binary size and
time-to-`/healthz`.

## Why https

`apps/web` cannot serve a rendered page over http. The antiforgery cookie is
`__Host-nw-antiforgery` with `CookieSecurePolicy.Always` (`apps/web/Program.cs:462`) and
`Layout.Page` mints a token on every page, so over http every render throws and answers 500 —
measured, 245,106 of them, before `cert.ts` existed. The harness mints a throwaway CA and leaf
with `openssl` into `tmp/bench/` and trusts that CA for its own requests only. `openssl` is
therefore a prerequisite; Git for Windows ships one and the resolver looks there.

Minting stages into `tmp/bench/mint/` and moves the four files into place at the end, and reuse
checks that the leaf still chains to the CA on disk rather than only that both files exist. An
interrupted mint used to leave a new CA beside the previous leaf — all four present, both
unexpired — and every later run reused that set, serving a certificate the driver's trust anchor
never signed. That surfaces as every request failing as a generic transport error, for up to a
year, with nothing in the output naming the certificate.

The page rate limiter is raised for the run (`NativeWeb:RateLimit:Page:PermitLimit`) because
`GET /` is behind `RequireRateLimiting("page")` at a limit sized for one human per minute.
Telemetry is off: an exporter retrying against a collector that is not there is work on the
process being measured.

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
