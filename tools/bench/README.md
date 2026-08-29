# tools/bench — the load harness spec 012 R16 asked for

A reproducible throughput and memory measurement of `apps/web`, against a committed baseline.
Run it before and after anything that could move the server: a .NET upgrade, a middleware
change, a rewrite of the render path.

```
deno run -A tools/bench/run.ts                  measure, report, compare for information
deno run -A tools/bench/run.ts --check          the same, but a regression exits 1
deno run -A tools/bench/run.ts --baseline       record this measurement as the new baseline
deno run -A tools/bench/run.ts --skip-publish   reuse the existing publish output
```

Other flags: `--duration <seconds>`, `--runs <n>`, `--concurrency <n>`, `--port <n>`. Changing
any of the three harness settings makes the run non-comparable to the baseline, and the tool
says so rather than comparing anyway.

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

**Small regressions.** Eight `home` passes on one machine and one unchanged binary spread
13.8%; `healthz` spread 9.4%; peak RSS spread 24%. The shipped tolerances are 15% down on
throughput and 25% up on RSS, sized from that. Anything smaller is under the noise floor. Run
it on an otherwise idle machine and re-record the baseline when the hardware changes.

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

The page rate limiter is raised for the run (`NativeWeb:RateLimit:Page:PermitLimit`) because
`GET /` is behind `RequireRateLimiting("page")` at a limit sized for one human per minute.
Telemetry is off: an exporter retrying against a collector that is not there is work on the
process being measured.

## baseline.json

Carries the measurement, the harness settings, and a machine fingerprint (hostname, core
count, OS, arch). A run on any other machine, or with different settings, prints its numbers
and says there is nothing to compare against — it does not compare and it does not fail.

`--baseline` refuses to write a measurement with failed requests, zero throughput, or no RSS
reading. That guard exists because the first real run of this harness recorded exactly such a
baseline: 0 req/s and 245,106 errors, written without complaint, under which the floor would
have been unfailable forever.
