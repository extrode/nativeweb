import {
  assert,
  assertEquals,
  assertRejects,
  assertStringIncludes,
  assertThrows,
} from "@std/assert";

import {
  assertPortFree,
  benchEnv,
  drainInto,
  findPublishedExe,
  LOG_TAIL_LINES,
  PUBLISH_DIR,
  waitForHealthz,
} from "./server.ts";
import { removeScratch, scratchDir } from "./scratch.ts";

Deno.test("waitForHealthz returns once /healthz answers 200", async () => {
  let attempts = 0;
  const elapsed = await waitForHealthz(
    "http://stub",
    1000,
    (input) => {
      assertEquals(String(input), "http://stub/healthz");
      attempts++;
      return Promise.resolve(new Response("ok", { status: attempts < 3 ? 503 : 200 }));
    },
    () => Promise.resolve(),
    (() => {
      let t = 0;
      return () => (t += 1);
    })(),
  );

  assertEquals(attempts, 3);
  // The stub clock ticks once per read: the start, one loop-condition read per attempt, and
  // the read that produces the return value. A returned 0 or NaN would fail here.
  assertEquals(elapsed, 4);
});

Deno.test("a connection that is never accepted times out with the budget named", async () => {
  let t = 0;
  const error = await assertRejects(
    () =>
      waitForHealthz(
        "http://stub",
        500,
        () => Promise.reject(new TypeError("refused")),
        () => Promise.resolve(),
        () => (t += 100),
      ),
    Error,
  );

  assertStringIncludes(error.message, "500ms");
});

Deno.test("a process that died on startup is reported by its exit code, not as a hang", async () => {
  let polls = 0;
  const error = await assertRejects(
    () =>
      waitForHealthz(
        "http://stub",
        60_000,
        () => {
          polls++;
          return Promise.reject(new TypeError("refused"));
        },
        () => Promise.resolve(),
        (() => {
          let t = 0;
          return () => (t += 1);
        })(),
        // 78 is StartupConfigGuards.FailStartup's code: a refused boot, not a slow one.
        () => (polls >= 2 ? 78 : null),
      ),
    Error,
  );

  assertStringIncludes(error.message, "exited with code 78");
  // It stopped as soon as the process was gone rather than waiting out the full minute.
  assertEquals(polls, 2);
});

Deno.test("the bench environment silences telemetry and forces the in-memory store", () => {
  const env = benchEnv("http://localhost:5997");

  assertEquals(env.ASPNETCORE_URLS, "http://localhost:5997");
  // Inert against apps/sample, which registers no telemetry -- it was live when the target was
  // apps/web. Kept because the harness measures whatever it is pointed at, and an exporter
  // waking up mid-run is the kind of thing that moves a number without explaining itself.
  assertEquals(env.Sample__Telemetry__Enabled, "false");
  // Warning and above: at default level the app writes an info line per request and draining
  // it cost 14% of measured throughput, measured on apps/web before it left the repo.
  assertEquals(env.Logging__LogLevel__Default, "Warning");
  // Empty, not absent: Deno.Command adds to the inherited environment rather than replacing
  // it, so this is the one value that reliably wins over whatever the invoking shell exports.
  assertEquals(env.ConnectionStrings__Sample, "");
});

Deno.test("findPublishedExe names the directory it looked in when nothing is published", async () => {
  const root = await scratchDir();
  try {
    const error = await assertRejects(() => findPublishedExe(root), Error);
    assertStringIncludes(error.message, PUBLISH_DIR);
    assertStringIncludes(error.message, "--skip-publish");
  } finally {
    await removeScratch(root);
  }
});

Deno.test("findPublishedExe finds the binary in the harness's own publish directory", async () => {
  const root = await scratchDir();
  try {
    const exeName = Deno.build.os === "windows" ? "nativeweb.sample.exe" : "nativeweb.sample";
    const dir = `${root}/${PUBLISH_DIR}`;
    await Deno.mkdir(dir, { recursive: true });
    await Deno.writeTextFile(`${dir}/${exeName}`, "");

    assertStringIncludes(await findPublishedExe(root), exeName);
  } finally {
    await removeScratch(root);
  }
});

// The regression this whole change exists to stop. Before it, findPublishedExe scanned
// apps/sample/bin/Release/net10.0/*/publish -- which is the e2e fixture's output directory --
// so a --skip-publish run would silently benchmark whatever binary that tier last built and
// report the number as the harness's own. It must now refuse.
Deno.test("the e2e fixture's publish output is not mistaken for the harness's", async () => {
  const root = await scratchDir();
  try {
    const exeName = Deno.build.os === "windows" ? "nativeweb.sample.exe" : "nativeweb.sample";
    const e2eDir = `${root}/apps/sample/bin/Release/net10.0/win-x64/publish`;
    await Deno.mkdir(e2eDir, { recursive: true });
    await Deno.writeTextFile(`${e2eDir}/${exeName}`, "");

    const error = await assertRejects(() => findPublishedExe(root), Error);
    assertStringIncludes(error.message, "belongs to the e2e fixture");
  } finally {
    await removeScratch(root);
  }
});

// A directory with no binary in it is not a publish. Same shape as the RID-directory case
// this replaced: the check is the file, not the folder.
Deno.test("an empty publish directory is not mistaken for a published binary", async () => {
  const root = await scratchDir();
  try {
    await Deno.mkdir(`${root}/${PUBLISH_DIR}`, { recursive: true });
    await assertRejects(() => findPublishedExe(root), Error);
  } finally {
    await removeScratch(root);
  }
});

Deno.test("publish output is pinned outside every tree git can see", () => {
  // tmp/ is gitignored by rule; the repo-root publish/ deliberately is not, because
  // justsmtp commits one. A throwaway NativeAOT publish landing where `git add -A` can
  // see it has cost this repo a bad commit once already.
  assertStringIncludes(PUBLISH_DIR, "tmp");
});

Deno.test("the log tail keeps the last lines and drops the rest", async () => {
  const tail: string[] = [];
  const lines = Array.from({ length: LOG_TAIL_LINES + 5 }, (_, i) => `line ${i}`);
  const stream = ReadableStream.from([new TextEncoder().encode(lines.join("\n") + "\n")]);

  await drainInto(stream, tail);

  assertEquals(tail.length, LOG_TAIL_LINES);
  // The tail is what a failure needs: the END of the log, where the exception is.
  assertEquals(tail.at(-1), `line ${lines.length - 1}`);
  assertEquals(tail[0], `line ${lines.length - LOG_TAIL_LINES}`);
});

Deno.test("a line split across two chunks is reassembled, not torn in half", async () => {
  const tail: string[] = [];
  const encoder = new TextEncoder();
  const stream = ReadableStream.from([
    encoder.encode("System.InvalidOper"),
    encoder.encode("ationException: boom\n"),
  ]);

  await drainInto(stream, tail);

  assertEquals(tail, ["System.InvalidOperationException: boom"]);
});

Deno.test("a port already in use is named, not left for a 60s healthz timeout", () => {
  const listener = Deno.listen({ port: 0, hostname: "127.0.0.1" });
  const port = (listener.addr as Deno.NetAddr).port;
  try {
    const error = assertThrows(() => assertPortFree(port), Error);
    // The stale process is the failure this names: without it, waitForHealthz gets a 200 from
    // whatever is already listening and the harness benchmarks last week's binary.
    assertStringIncludes(error.message, String(port));
    assertStringIncludes(error.message, "--port");
  } finally {
    listener.close();
  }
});

Deno.test("a free port passes the pre-flight and is released again", () => {
  const listener = Deno.listen({ port: 0, hostname: "127.0.0.1" });
  const port = (listener.addr as Deno.NetAddr).port;
  listener.close();

  assertPortFree(port);
  // Released, not held: the app itself has to bind it a moment later.
  assertPortFree(port);
});
