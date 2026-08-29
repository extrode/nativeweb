import { assert, assertEquals } from "@std/assert";

import { drive } from "./load.ts";

/** A fake clock the workers advance themselves, so no test sleeps. */
function fakeClock(stepMs: number) {
  let t = 0;
  return {
    now: () => t,
    advance: () => {
      t += stepMs;
    },
  };
}

Deno.test("drive counts 2xx responses as requests and stops at the deadline", async () => {
  const clock = fakeClock(1);
  let calls = 0;

  const sample = await drive({
    url: "http://stub/",
    concurrency: 1,
    durationMs: 10,
    now: clock.now,
    fetchImpl: () => {
      calls++;
      clock.advance();
      return Promise.resolve(new Response("ok"));
    },
  });

  assertEquals(sample.errors, 0);
  assertEquals(sample.requests, calls);
  // One worker, 1ms per request, a 10ms budget: the deadline check happens before each
  // request, so exactly ten fit.
  assertEquals(sample.requests, 10);
  assertEquals(sample.latenciesMs.length, 10);
});

Deno.test("a 500 is an error, not throughput", async () => {
  const clock = fakeClock(1);

  const sample = await drive({
    url: "http://stub/",
    concurrency: 1,
    durationMs: 5,
    now: clock.now,
    fetchImpl: () => {
      clock.advance();
      return Promise.resolve(new Response("boom", { status: 500 }));
    },
  });

  assertEquals(sample.requests, 0);
  assertEquals(sample.errors, 5);
});

Deno.test("a refused connection is counted, not swallowed", async () => {
  const clock = fakeClock(1);

  const sample = await drive({
    url: "http://stub/",
    concurrency: 1,
    durationMs: 3,
    now: clock.now,
    fetchImpl: () => {
      clock.advance();
      return Promise.reject(new TypeError("connection refused"));
    },
  });

  assertEquals(sample.requests, 0);
  assertEquals(sample.errors, 3);
  // A failed request has no meaningful latency, so it does not pollute the percentiles.
  assertEquals(sample.latenciesMs.length, 0);
});

Deno.test("every worker drives", async () => {
  const clock = fakeClock(1);
  const seen = new Set<number>();
  let next = 0;

  await drive({
    url: "http://stub/",
    concurrency: 4,
    durationMs: 4,
    now: clock.now,
    fetchImpl: () => {
      // Each worker's first call happens before any clock advance, so four distinct ids
      // are handed out before the deadline can be reached.
      seen.add(next++);
      clock.advance();
      return Promise.resolve(new Response("ok"));
    },
  });

  assert(seen.size >= 4, `expected at least one request per worker, saw ${seen.size}`);
});

Deno.test("the body is consumed so the connection returns to the pool", async () => {
  const clock = fakeClock(5);
  const bodies: Response[] = [];

  await drive({
    url: "http://stub/",
    concurrency: 1,
    durationMs: 5,
    now: clock.now,
    fetchImpl: () => {
      const response = new Response("payload");
      bodies.push(response);
      clock.advance();
      return Promise.resolve(response);
    },
  });

  assertEquals(bodies.length, 1);
  assertEquals(bodies[0].bodyUsed, true);
});

Deno.test("elapsedMs is the real elapsed time, not the requested duration", async () => {
  const clock = fakeClock(7);

  const sample = await drive({
    url: "http://stub/",
    concurrency: 1,
    durationMs: 10,
    now: clock.now,
    fetchImpl: () => {
      clock.advance();
      return Promise.resolve(new Response("ok"));
    },
  });

  // Two requests at 7ms each overshoot the 10ms budget; reporting 10 would inflate the rate.
  assertEquals(sample.elapsedMs, 14);
});

Deno.test("drives a real socket end to end", async () => {
  const controller = new AbortController();
  let served = 0;
  const server = Deno.serve(
    { port: 0, signal: controller.signal, onListen: () => {} },
    () => {
      served++;
      return new Response("hello");
    },
  );

  try {
    const sample = await drive({
      url: `http://127.0.0.1:${server.addr.port}/`,
      concurrency: 2,
      durationMs: 200,
    });

    assertEquals(sample.errors, 0);
    assertEquals(sample.requests, served);
    assert(sample.requests > 0, "expected the stub server to have been hit");
  } finally {
    controller.abort();
    await server.finished;
  }
});
