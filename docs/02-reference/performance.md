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
| 2026-08-31 | **Compression moved to Caddy** — current baseline | **16,528.8** | 0.93ms | 2.40ms | 23,183.5 | 54.0 MB | 0.713 |
| 2026-08-31 | Framework web pipeline adopted (spec 015), compressing in-process | 6,440.5 | 2.30ms | 6.52ms | 19,539.8 | 53.5 MB | 0.330 |
| 2026-08-31 | Phase 1 close: review fixes (author match, `Take` echoed) | 18,958.6 | 0.70ms | 2.30ms | 23,324.5 | 35.6 MB | 0.813 |
| 2026-08-31 | **Content-Length declared** on template pages | 19,051.8 | 0.69ms | 2.30ms | 22,655.4 | 35.4 MB | 0.841 |
| 2026-08-31 | Rendering through compiled templates (T3/T4) | 12,124.2 | 1.18ms | 3.76ms | 22,523.7 | 38.7 MB | 0.538 |
| 2026-08-31 | Design system + styled page (T1/T2) | 18,453.4 | 0.67ms | 2.74ms | 20,953.5 | 47.0 MB | 0.881 |
| 2026-08-31 | Baseline, hand-written StringBuilder renderer | 19,937.6 | — | — | 23,068.6 | 45.0 MB | 0.864 |

Zero errors in every run of every row.

**Where it stands.** The top row is the current state and the committed baseline. A pipeline of
ten middleware where there were none costs `articles` **12.8%** (18,958.6 → 16,528.8) and
`healthz` nothing measurable (23,324.5 → 23,183.5, inside the run-to-run spread). Peak RSS rose
35.6 → 54.0 MB, which is ten registrations of services that were absent before.

The second row is what the same pipeline cost while it compressed in-process, and it is kept
because the difference between the two rows is the whole of bottleneck 4: one middleware was
worth 157% of this page's throughput. `tools/bench/baseline.json` was re-recorded twice on
2026-08-31 — once for the pipeline as first adopted, once after compression moved — because a
baseline that is red on every run gates nothing.

Between the third and fourth rows, two behavioural fixes, not a perf change: matching authors as
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

### 4. Response compression — 63% of `articles` throughput (fixed, by moving it)

`AddNativeWebResponseCompression` registered Brotli and gzip for `text/html`, and
`UseResponseCompression` ran on every page. It was the single most expensive step in the pipeline
by a wide margin. Four variants of the same page, same harness, same session:

| variant | `articles` req/s | p50 | `healthz` req/s |
|---|---|---|---|
| pipeline with `UseResponseCompression()` commented out | 17,613.3 | 0.88ms | 19,998.8 |
| gzip only, `CompressionLevel.Fastest` | 9,170.4 | 1.57ms | 19,926.1 |
| Brotli + gzip, `CompressionLevel.Fastest` | 6,555.4 | 2.27ms | 19,892.2 |
| Brotli + gzip, provider defaults — as first shipped | 6,586.2 | 2.26ms | 19,853.4 |

Three readings, in order of how much they change the options:

- **The compression level is not the cost.** `Fastest` and the default are 6,555.4 against
  6,586.2 — inside the noise. The page is 3,271 bytes; what is being paid for is the per-response
  codec setup, not the entropy coding, which is why turning the quality knob down buys nothing.
- **The codec is.** Dropping Brotli and keeping gzip at `Fastest` recovers 40% of what was lost.
- **Bottleneck 1 comes back through the middleware.** Compressing strips the `Content-Length`
  `TemplateResults` was fixed to declare, so every compressed page is chunked again. Nothing can
  declare a length ahead of a stream-compressed body, so this half is not tunable — only
  avoidable.

`healthz` is flat across all four rows, which is what confirms the attribution: the mime list is
`text/html` and a plaintext route never enters the middleware.

#### The A/B that settled it

Compression has to happen somewhere, so the question was never whether to pay but where. Measured
through a real Caddy 2.11.4 in front of the published binary, same driver, same three-runs-of-10s
at concurrency 16:

| variant | `articles` req/s | p50 | `healthz` req/s |
|---|---|---|---|
| A — app compresses, direct to Kestrel | 7,468.0 | 2.01ms | 20,804.9 |
| B — nothing compresses, direct to Kestrel | 18,216.1 | 0.88ms | 21,118.8 |
| C — app compresses, through Caddy | 6,254.6 | 2.50ms | 15,777.7 |
| D — **Caddy compresses (`encode zstd gzip`), app does not** | **9,002.1** | 1.68ms | 16,470.1 |
| E — nothing compresses, through Caddy | 14,253.4 | 1.06ms | 15,973.9 |

C and D are the two real deployment shapes, and D beats C by **44%**. The rest is what makes that
number readable: the proxy hop alone costs 22% (B → E), compressing at the edge costs 37% of what
is left (E → D), and compressing in the app costs 59% (B → A) for a worse result. The edge also
gets **zstd**, which .NET has no provider for: 3,271 bytes of HTML leave Caddy as 1,324.

Two structural reasons on top of the number, both found in review rather than in the numbers:

- `UseResponseCompression` sat **above** `UseOutputCache` in the pipeline order, so every
  output-cache hit was recompressed. The cache never recovered the codec cost for exactly the
  public traffic it exists to serve, and moving compression below the cache in-app would have
  forced `Accept-Encoding` into the cache key.
- Buffering in a middleware to restore `Content-Length` is not an option here:
  `TemplateWriter` streams UTF-8 to the body writer by design (spec 003 R41), and these pages only
  behave as materialized because 3 KB fits in Kestrel's pipe before a flush. That is an emergent
  property, not a contract.

**What changed.** `UseNativeWebPipeline` no longer compresses; `scripts/deploy/caddy/Caddyfile`
and `Caddyfile.edge` carry `encode zstd gzip`, and both validate against Caddy 2.11.4.
`AddNativeWebResponseCompression` stays public and registered, so a host with no compressing proxy
adds one line and gets the old behaviour back — asserted by `CompressionPlacementTests`, because
losing the `encode` line would ship every page uncompressed with nothing failing.

Brotli is absent from the directive rather than declined: stock Caddy has no encoder for it, and
adding `br` fails config validation with `module not registered: http.encoders.br`.

**What the numbers still do not show.** The harness measures on loopback with no bandwidth cost,
so it flatters every variant that skips compression; on a real WAN client the transfer of 3,271
bytes against 1,324 dominates a 1.4 ms p50 difference. And 16 workers on 16 cores is CPU
saturation, which a single-host product will not reach — the throughput deltas are an upper bound
on what this matters in production, except for the cache-hit recompression, which scaled with
exactly the traffic the cache exists for.

## What has never been measured

- **Under load with PostgreSQL.** Every row here runs against the in-memory store, so the
  measurement is the render path and the pipeline, not the query path. A JauntyQ-backed run is a
  different harness and a different baseline.
- **On the deployment target.** `docs/02-reference/deployment.md` still reads `NOT YET MEASURED`
  for every elapsed time, and spec 011's T15 has never run on a real host.
- **Concurrency other than 16.** The harness takes `--concurrency`; nothing has used it.
