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
| 2026-08-31 | **Content-Length declared** on template pages | **19,051.8** | 0.69ms | 2.30ms | 22,655.4 | 35.4 MB | **0.841** |
| 2026-08-31 | Rendering through compiled templates (T3/T4) | 12,124.2 | 1.18ms | 3.76ms | 22,523.7 | 38.7 MB | 0.538 |
| 2026-08-31 | Design system + styled page (T1/T2) | 18,453.4 | 0.67ms | 2.74ms | 20,953.5 | 47.0 MB | 0.881 |
| 2026-08-31 | Baseline, hand-written StringBuilder renderer | 19,937.6 | — | — | 23,068.6 | 45.0 MB | 0.864 |

Zero errors in every run of every row.

**Where it stands.** `articles` is 4.4% under the pre-Phase-1 baseline and `healthz` is 8.1% over
it; both sit inside the 16.3% cross-session spread the harness documents, and the ratio is back to
0.841 against the baseline's 0.864. A page that renders twenty rows through the layout, the
template engine and an antiforgery mint costs about 16% against a route that renders nothing.

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

## What has never been measured

- **Under load with PostgreSQL.** Every row here runs against the in-memory store, so the
  measurement is the render path and the pipeline, not the query path. A JauntyQ-backed run is a
  different harness and a different baseline.
- **On the deployment target.** `docs/02-reference/deployment.md` still reads `NOT YET MEASURED`
  for every elapsed time, and spec 011's T15 has never run on a real host.
- **Concurrency other than 16.** The harness takes `--concurrency`; nothing has used it.
