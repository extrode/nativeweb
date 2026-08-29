// The load driver: N workers hammering one URL over a fixed duration.
//
// Deno's own fetch, not wrk or bombardier, and not because those would be worse -- they would
// be better. Neither is installed on this machine, and the repo's tooling rule is Deno over
// anything that needs npm or a binary nobody can reproduce. What that costs is absolute
// ceiling: this driver spends real time in JS per request, so its req/s is a floor for the
// server, not the server's limit. What it buys is a harness that runs on a clean checkout with
// `deno run` and nothing else, which is the property R16 actually needed.

import type { RunSample } from "./stats.ts";

export interface LoadOptions {
  url: string;
  concurrency: number;
  durationMs: number;
  /** Seam for tests; defaults to global fetch. */
  fetchImpl?: typeof fetch;
  /** Seam for tests; defaults to performance.now. */
  now?: () => number;
}

/**
 * Drive one timed pass. Resolves when the duration elapses and every in-flight request has
 * settled -- a worker checks the deadline between requests, so the pass runs slightly over.
 * The measured elapsed time is the real one, not the requested one, so that overrun does not
 * inflate the rate.
 */
export async function drive(options: LoadOptions): Promise<RunSample> {
  const fetchImpl = options.fetchImpl ?? fetch;
  const now = options.now ?? (() => performance.now());

  const latenciesMs: number[] = [];
  let requests = 0;
  let errors = 0;

  const start = now();
  const deadline = start + options.durationMs;

  const worker = async () => {
    while (now() < deadline) {
      const began = now();
      try {
        const response = await fetchImpl(options.url);
        // The body has to be consumed or the connection is not returned to the pool, and
        // every request after the first few would open a fresh socket -- which would make
        // this a measurement of TCP setup rather than of the server.
        await response.arrayBuffer();
        latenciesMs.push(now() - began);
        if (response.ok) requests++;
        else errors++;
      } catch {
        // A refused or reset connection is a failed request, not an absent one. Counting it
        // as neither would let a server that died halfway through report a healthy rate over
        // the half it was alive.
        errors++;
      }
    }
  };

  await Promise.all(Array.from({ length: options.concurrency }, () => worker()));

  return { requests, errors, latenciesMs, elapsedMs: now() - start };
}
