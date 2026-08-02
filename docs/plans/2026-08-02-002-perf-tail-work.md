# Perf tail work — research findings and approved scope

Date: 2026-08-02. Standard tier; scope approved by the user in-session (items 1–3 now,
item 5 as its own spec, item 4 explicitly not selected).

## Baseline (k6, 32 VUs, 15s/scenario, HTTPS localhost:5346, NativeAOT Release `0.1.0+929a2c9`)

| Scenario | med | p95 | Notes |
| --- | --- | --- | --- |
| healthz | 0.56ms | 2.24ms | |
| static css | 1.83ms | 3.48ms | fingerprinted, precompressed |
| static htmx.min.js | 2.99ms | 6.50ms | |
| home `/` | 0.90ms | 2.00ms | SSR, no DB |
| sign-in | 0.85ms | 1.74ms | SSR, no DB |
| pub_list `/u/sy` | 7.41ms | 20.56ms | 4 sequential DB round trips |
| pub_tag `/u/sy/t/{tag}` | 4.43ms | 12.75ms | same shape, smaller result |

2.0M requests, 0 failures, ~17,050 rps aggregate. Two research workers (stack pitfalls;
fast-framework/TechEmpower techniques) agreed: the no-DB pages are at the realistic floor
(~1–4ms for HTTPS SSR+DB was the researched floor; we are under it for no-DB pages), so the
only headroom is the DB-backed list pages — specifically the median→p95 gap.

## Already done (verified in code, no action)

- Npgsql auto-prepare (`MaxAutoPrepare=20`, R35), pooled `NpgsqlDataSource`.
- `MapStaticAssets`: content-hash fingerprints, immutable caching, build-time precompression.
- Brotli response compression scoped to `text/html` only; BREACH acceptance documented
  (docs/conventions.md). Not widened.
- `[LoggerMessage]` source-generated one-line request logging (R23).
- Kestrel Date-header caching (framework default), deliberate middleware order.
- JIT/PGO knobs are N/A under NativeAOT.

## Rejected (benchmark-only or security-negative — not doing)

- Hand-rolled routing, stripped DB drivers, disabling telemetry/logging, TLS-resume games,
  removing security headers, weakening antiforgery/limits. TechEmpower's own maintainers mark
  several of these "stripped"; none survive contact with production or the security posture.

## Approved scope (this plan) — outcomes (implemented 2026-08-02, merged as manzil 8c19261)

1. **Pool idle floor — SHIPPED.** `Minimum Pool Size` floor of 8 (valid `Manzil:Db:MinPoolSize`
   wins; an explicit connection-string value is respected via a `ShouldSerialize` probe, pinned
   by a unit test; writes clamped to `Maximum Pool Size` so min>max cannot crash boot). Npgsql
   fills lazily, so this prevents idle pruning rather than pre-warming. No measurable effect in
   tonight's noise; kept as cheap, sound tail insurance.
2. **Parallelize pub_list/pub_tag queries — MEASURED AND REVERTED.** 3-way fan-out: med
   7.41→10.12ms, p95 20.6→57.4ms (32 VUs × 3 connections ≈ 96 saturates the pool of 100 and
   the local Postgres). 2-way: still no better than sequential. Solo (1 VU): 2.8ms vs
   sequential 2.58ms — no benefit even without load, because each extra pooled-connection
   acquisition costs more than the ~0.3ms query it overlaps. Endpoint left unchanged; the
   page-clamp behavior gained integration coverage.
3. **GC mode A/B — Workstation GC SHIPPED.** Two Workstation runs (7.21/29.4, 7.69/28.2 med/p95
   ms) beat two default-Server-GC runs (9.05/32.8, 10.35/45.2) on the identical build the same
   evening; baked via `<ServerGarbageCollection>false</ServerGarbageCollection>` in
   manzil.csproj. Caveat recorded in the csproj: whether `DOTNET_gcServer=1` can override a
   NativeAOT-baked value is unverified — plan a rebuild for a many-core host. Second caveat:
   the box was noisy (±30% between identical runs, aggregate rps swinging 12.6k–18.2k), so
   the GC delta is directional, not precise; re-measure before the epass.io cutover.

All manzil suites green (231 unit / 93 integration / 21 e2e); reviewed (opus, two rounds — pool
min>max boot-crash and four comment/coverage findings fixed).

## Recorded, not selected

4. **Fold count into page query** (`COUNT(*) OVER()`) — one fewer round trip but reverses the
   deliberate separate-count design (counts repeat their list predicate verbatim) reviewed
   2026-08-02. Revisit only if 1–3 leave pub_list unsatisfactory.

## Approved as a future spec (Durable tier, not started)

5. **Cacheable anonymous pages** — `/` and `/u/*` mint a per-request antiforgery token + CSP
   nonce, making the crawlable pages uncacheable (long-standing todo item). Removing
   per-request state from anonymous pages could take pub_list toward ~1ms, but changes how the
   sign-in form obtains its token and how public pages carry CSP (nonce → hash or static
   allowlist). Security boundary unchanged: antiforgery remains on every authenticated /
   state-changing route. Needs its own spec + design; user approved drafting it.
