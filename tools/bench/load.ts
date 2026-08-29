// The load driver: N workers hammering one URL over a fixed duration.
//
// Deno's own fetch, not wrk or bombardier, and not because those would be worse -- they would
// be better. Neither is installed on this machine, and the repo's tooling rule is Deno over
// anything that needs npm or a binary nobody can reproduce. What that costs is absolute
// ceiling: this driver spends real time in JS per request, so its req/s is a floor for the
// server, not the server's limit. What it buys is a harness that runs on a clean checkout with
// `deno run` and nothing else, which is the property R16 actually needed.

import type { RunSample } from "./stats.ts";

/** Whatever the injected timer returns; Deno's setTimeout is not typed as a number here. */
type Timer = ReturnType<typeof setTimeout>;

/** How long past the deadline an in-flight request may take before it is aborted. */
export const GRACE_MS = 30_000;

export interface LoadOptions {
  url: string;
  concurrency: number;
  durationMs: number;
  /** Seam for tests; defaults to global fetch. */
  fetchImpl?: typeof fetch;
  /** Seam for tests; defaults to performance.now. */
  now?: () => number;
  /** Seam for tests; defaults to setTimeout. */
  setTimer?: (callback: () => void, ms: number) => Timer;
  clearTimer?: (handle: Timer) => void;
}

/**
 * Drive one timed pass. Resolves when the duration elapses and every in-flight request has
 * settled -- a worker checks the deadline between requests, so the pass runs slightly over.
 * The measured elapsed time is the real one, not the requested one, so that overrun does not
 * inflate the rate.
 *
 * One shared abort, armed once for the whole pass rather than per request: a server that
 * accepts the connection and then never answers would otherwise leave a worker awaiting
 * forever, and the deadline is only checked between requests. Per-request `AbortSignal.timeout`
 * would arm a timer twenty thousand times a second on the machine being measured.
 */
export async function drive(options: LoadOptions): Promise<RunSample> {
  const fetchImpl = options.fetchImpl ?? fetch;
  const now = options.now ?? (() => performance.now());
  const setTimer = options.setTimer ?? ((callback, ms) => setTimeout(callback, ms));
  const clearTimer = options.clearTimer ?? ((handle) => clearTimeout(handle));

  const latenciesMs: number[] = [];
  let requests = 0;
  let errors = 0;

  const start = now();
  const deadline = start + options.durationMs;

  const abort = new AbortController();
  const guard = setTimer(() => abort.abort(), options.durationMs + GRACE_MS);

  const worker = async () => {
    while (now() < deadline && !abort.signal.aborted) {
      const began = now();
      try {
        const response = await fetchImpl(options.url, { signal: abort.signal });
        // The body has to be consumed or the connection is not returned to the pool, and
        // every request after the first few would open a fresh socket -- which would make
        // this a measurement of TCP setup rather than of the server.
        await response.arrayBuffer();
        if (response.ok) {
          requests++;
          // Latency is recorded for successful responses only. A 500 costs the server
          // almost nothing to produce, so counting its round trip would make a broken run
          // look faster than a working one.
          latenciesMs.push(now() - began);
        } else {
          errors++;
        }
      } catch {
        // A refused, reset or aborted connection is a failed request, not an absent one.
        // Counting it as neither would let a server that died halfway through report a
        // healthy rate over the half it was alive.
        errors++;
      }
    }
  };

  try {
    await Promise.all(Array.from({ length: options.concurrency }, () => worker()));
  } finally {
    clearTimer(guard);
  }

  return { requests, errors, latenciesMs, elapsedMs: now() - start };
}
