import { assertEquals } from "@std/assert";

import { parsePs, parseTasklist, startRssSampler } from "./rss.ts";

Deno.test("tasklist CSV parses to bytes, thousands separator and all", () => {
  const line = '"nativeweb.web.exe","12345","Console","1","51,234 K"\r\n';
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

Deno.test("the sampler keeps the peak, not the last reading", async () => {
  const readings = [10, 90, 30];
  let i = 0;
  const sampler = startRssSampler(1, 0, () => Promise.resolve(readings[i++] ?? null));

  // Let the loop run through all three readings.
  await new Promise((resolve) => setTimeout(resolve, 20));
  assertEquals(await sampler.stop(), 90);
});

Deno.test("failed samples are skipped, not treated as zero", async () => {
  const readings: (number | null)[] = [null, 77, null];
  let i = 0;
  const sampler = startRssSampler(1, 0, () => Promise.resolve(readings[i++] ?? null));

  await new Promise((resolve) => setTimeout(resolve, 20));
  assertEquals(await sampler.stop(), 77);
});

Deno.test("a sampler that never read anything reports 0", async () => {
  const sampler = startRssSampler(1, 0, () => Promise.resolve(null));
  await new Promise((resolve) => setTimeout(resolve, 5));
  assertEquals(await sampler.stop(), 0);
});
