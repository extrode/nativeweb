# Performance log

Every load measurement taken on this repository, newest first, with what changed between one row
and the next. `tools/bench/README.md` explains the harness; this file is the record it produces.

**Numbers here are machine-bound and comparable only to each other.** All of them were taken on
`bench-host` (16 logical cores, Windows 11 10.0.26200), against `apps/sample` published Release with
NativeAOT, over plain HTTP on loopback, 16 concurrent workers, three timed runs of 10 s averaged
after a discarded warmup. `tools/bench/baseline.json` carries the machine fingerprint and the
harness records rather than compares when it does not match.

**`healthz` is the control.** It renders nothing, so it measures the driver, the socket and
Kestrel's floor. A change that moves `articles` and `healthz` by the same proportion did not cost
anything in the render path — it moved the whole machine. Only the ratio between them says
anything about a page.

## Measurements

| Date | Change | `articles` req/s | p50 | p99 | `healthz` req/s | peak RSS | `articles`/`healthz` |
|---|---|---|---|---|---|---|---|
| 2026-08-31 | **Framework web pipeline adopted** (spec 015) — new baseline | **6,440.5** | 2.30ms | 6.52ms | 19,539.8 | 53.5 MB | 0.330 |
| 2026-08-31 | Phase 1 close: review fixes (author match, `Take` echoed) | 18,958.6 | 0.70ms | 2.30ms | 23,324.5 | 35.6 MB | 0.813 |
| 2026-08-31 | **Content-Length declared** on template pages | 19,051.8 | 0.69ms | 2.30ms | 22,655.4 | 35.4 MB | 0.841 |
| 2026-08-31 | Rendering through compiled templates (T3/T4) | 12,124.2 | 1.18ms | 3.76ms | 22,523.7 | 38.7 MB | 0.538 |
| 2026-08-31 | Design system + styled page (T1/T2) | 18,453.4 | 0.67ms | 2.74ms | 20,953.5 | 47.0 MB | 0.881 |
| 2026-08-31 | Baseline, hand-written StringBuilder renderer | 19,937.6 | — | — | 23,068.6 | 45.0 MB | 0.864 |

Zero errors in every run of every row.

**Where it stands.** The top row is the current state and the committed baseline. Adopting the
framework pipeline cost `articles` **66% of its throughput** (18,958.6 → 6,440.5) and `healthz`
16% (23,324.5 → 19,539.8), and the two numbers are that far apart because the whole of the
`articles` loss is one middleware: response compression, whose mime list is `text/html` only, so
`healthz` never enters it. Bottleneck 4 below has the bisection. `tools/bench/baseline.json` was
re-recorded from this row on 2026-08-31 — the pre-pipeline figures would have made `--check` red
on every future run, which is a broken gate rather than a standing complaint.

The 16% on `healthz` is the rest of the pipeline — forwarded headers, the rate limiter, the
security headers, the output cache lookup and telemetry — and it holds with compression disabled
(19,998.8 req/s), so none of it is the codec. Peak RSS rose 35.6 → 53.5 MB for the same reason:
eleven registrations of services that were absent before.

Between the second and third rows, two behavioural fixes, not a perf change: matching authors as
well as titles adds one null-checked `Contains` per row over at most twenty rows, and echoing
`Take` adds one integer to the render. Both are inside the noise, which the ratio moving 0.841 →
0.813 across adjacent runs of near-identical code is itself a measure of.

## Bottlenecks found, in the order they were found

### 1. A chunked response, on every page — 35% of throughput (fixed)

`TemplateResults.Html` wrote into `Response.BodyWriter` and flushed once. That reads like the
streaming path, and is not: the body was fully materialized before the flush either way, so
nothing was ever being streamed. What the missing `Content-Length` did was make every page a
chunked response.

Three variants of the identical page separate the two candidate causes:

| variant | req/s | p50 |
|---|---|---|
| straight to `BodyWriter`, no length | 11,779 | 1.24ms |
| buffered first, still no length | 11,778 | 1.24ms |
| buffered first, length declared | 18,105 | 0.71ms |

The middle row is the one that matters. Buffering changes nothing, so the cost was never the many
small `GetSpan`/`Advance` calls into Kestrel's pipe — the theory that looked most likely going in.
It is the framing alone. Fixed in `libs/nativeweb.render/TemplateResults.cs`; guarded by
`TemplateHtmlResultTests`, because no assertion about markup can tell a chunked page from a
measured one.

A count off the wire is worth recording alongside it: the chunked response carried **two** chunk
headers, not one per write. Kestrel was already coalescing. The cost is the framing, not
fragmentation.

### 2. The driver, not the server (open, and probably not worth closing)

The harness is a Deno `fetch` loop, because no load tool is installed on this machine and Deno is
the mandated runtime. A route that renders nothing tops out around 22.7k req/s, which is a
statement about `fetch` and loopback rather than about Kestrel. Every number here is therefore a
floor on what the server can do, and small absolute differences between adjacent rows are noise.

This is the reason `healthz` is measured at all, and the reason the tolerances are as wide as
16.3% down on throughput. Replacing the driver would raise every number and change no conclusion
that has been drawn from them so far.

### 3. Two locale lookups per page (open, unmeasured)

`PublicArticleEndpoints.Page` calls `Locales.Get(http.GetLocale())` twice — once for the render
and once for the title. It is a dictionary lookup and almost certainly noise at this scale, but it
is the only known redundant work on the request path, so it is written down rather than assumed
away.

### 4. Response compression — 63% of `articles` throughput (open, needs a decision)

`AddNativeWebResponseCompression` registers Brotli and gzip for `text/html`, and
`UseResponseCompression` runs on every page. It is the single most expensive step in the pipeline
by a wide margin. Four variants of the same page, same harness, same session:

| variant | `articles` req/s | p50 | `healthz` req/s |
|---|---|---|---|
| pipeline with `UseResponseCompression()` commented out | 17,613.3 | 0.88ms | 19,998.8 |
| gzip only, `CompressionLevel.Fastest` | 9,170.4 | 1.57ms | 19,926.1 |
| Brotli + gzip, `CompressionLevel.Fastest` | 6,555.4 | 2.27ms | 19,892.2 |
| Brotli + gzip, provider defaults — **as shipped** | 6,586.2 | 2.26ms | 19,853.4 |

Three readings, in order of how much they change the options:

- **The compression level is not the cost.** `Fastest` and the default are 6,555.4 against
  6,586.2 — inside the noise. Turning the quality knob down, which is where this investigation
  started, buys nothing.
- **The codec is.** Dropping Brotli and keeping gzip at `Fastest` recovers 40% of what was lost
  (6,555.4 → 9,170.4) for a larger body on the wire.
- **Bottleneck 1 comes back through the middleware.** Compressing strips the `Content-Length`
  `TemplateResults` was fixed to declare, so every compressed page is chunked again. The 35%
  measured there accounts for roughly the 17,613 → ~11,800 part of the drop; the codec accounts
  for the rest. Nothing can declare a length ahead of a stream-compressed body, so this half is
  not tunable — only avoidable.

`healthz` is flat across all four rows, which is what confirms the attribution: the mime list is
`text/html` and a plaintext route never enters the middleware.

**The decision this is open on** is where compression belongs, and it is a CPU-versus-bandwidth
trade nobody should make silently. `scripts/deploy/caddy/Caddyfile` has no `encode` directive
today, so the app is the only thing compressing; static assets are precompressed at publish time
and served as `.br`/`.gz` by `MapStaticAssets` regardless of this setting, so only rendered HTML
is affected. Three ways out, none taken here: keep it (best bytes, worst CPU), drop Brotli for
gzip `Fastest` (+42% throughput, bigger bodies), or move `encode zstd br gzip` to Caddy and take
`UseResponseCompression` out of the pipeline (+174%, and one place doing the work instead of two).

## What has never been measured

- **Under load with PostgreSQL.** Every row here runs against the in-memory store, so the
  measurement is the render path and the pipeline, not the query path. A JauntyQ-backed run is a
  different harness and a different baseline.
- **On the deployment target.** `docs/02-reference/deployment.md` still reads `NOT YET MEASURED`
  for every elapsed time, and spec 011's T15 has never run on a real host.
- **Concurrency other than 16.** The harness takes `--concurrency`; nothing has used it.
