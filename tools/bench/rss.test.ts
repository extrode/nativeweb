import { assert, assertEquals } from "@std/assert";

import { parsePs, parseTasklist, startRssSampler } from "./rss.ts";

Deno.test("tasklist CSV parses to bytes, thousands separator and all", () => {
  const line = '"nativeweb.sample.exe","12345","Console","1","51,234 K"\r\n';
  assertEquals(parseTasklist(line), 51234 * 1024);
});

Deno.test("tasklist with a non-comma separator still parses", () => {
  // Locale decides the separator; the parser strips every non-digit rather than assuming.
  assertEquals(parseTasklist('"a","1","Console","1","51.234 K"'), 51234 * 1024);
});

Deno.test("tasklist's no-such-process message is null, not zero", () => {
  const message = "INFO: No tasks are running which match the specified criteria.";
  assertEquals(parseTasklist(message), null);
  assertEquals(parseTasklist(""), null);
});

Deno.test("ps output parses kilobytes to bytes", () => {
  assertEquals(parsePs("  51234\n"), 51234 * 1024);
});

Deno.test("ps output for a dead pid is null", () => {
  assertEquals(parsePs(""), null);
  assertEquals(parsePs("ps: no such process"), null);
});

/**
 * Drive the sampler off its own reads rather than off a sleep: with `setTimeout` racing the
 * assertions, a loaded machine runs fewer iterations than the test expects and "keeps the
 * peak" starts asserting 90 against a peak of 10.
 */
function sampledOnce(readings: (number | null)[]) {
  let i = 0;
  let signalDone: () => void;
  const done = new Promise<void>((resolve) => (signalDone = resolve));

  return {
    done,
    sample: () => {
      const value = i < readings.length ? readings[i] : null;
      i++;
      if (i >= readings.length) signalDone();
      return Promise.resolve(value);
    },
  };
}

Deno.test("the sampler keeps the peak, not the last reading", async () => {
  const stub = sampledOnce([10, 90, 30]);
  const sampler = startRssSampler(1, 0, stub.sample);

  await stub.done;
  assertEquals(await sampler.stop(), 90);
});

Deno.test("failed samples are skipped, not treated as zero", async () => {
  const stub = sampledOnce([null, 77, null]);
  const sampler = startRssSampler(1, 0, stub.sample);

  await stub.done;
  assertEquals(await sampler.stop(), 77);
});

Deno.test("a sampler that never read anything reports 0", async () => {
  const stub = sampledOnce([null, null]);
  const sampler = startRssSampler(1, 0, stub.sample);

  await stub.done;
  assertEquals(await sampler.stop(), 0);
});

Deno.test("a sampler whose reader throws still stops, with the peak it had", async () => {
  let calls = 0;
  const sampler = startRssSampler(1, 0, () => {
    calls++;
    if (calls === 1) return Promise.resolve(64);
    return Promise.reject(new Error("tasklist vanished"));
  });

  // A rejecting reader must not become an unhandled rejection, and must not lose the reading
  // that already succeeded.
  await new Promise((resolve) => setTimeout(resolve, 10));
  assertEquals(await sampler.stop(), 64);
});

Deno.test("stop() cancels the pending interval instead of waiting it out", async () => {
  let samples = 0;
  const sampler = startRssSampler(1234, 60_000, () => {
    samples++;
    return Promise.resolve(4096);
  });

  // Let the first sample land, so the loop is parked in its sleep.
  await new Promise((resolve) => setTimeout(resolve, 10));
  const start = performance.now();
  const peak = await sampler.stop();

  assertEquals(peak, 4096);
  assertEquals(samples, 1);
  // A minute's interval: waiting it out would hang the test run rather than fail it, so the
  // assertion is that stopping is immediate.
  assert(performance.now() - start < 1000, `stop() took ${performance.now() - start}ms`);
});
