// Publish apps/sample, launch the published binary, wait for it to answer, stop it again.
//
// The published Release output, not `dotnet run`: that is the binary a deployment runs, and
// it is the only one whose figures mean anything -- a Debug host is a different JIT, a
// different static-file provider and a different startup path. Same choice, for overlapping
// reasons, as tests/nativeweb.scaffold.tests.e2e/SampleProcessFixture.cs.
//
// http, not https. apps/sample uses default antiforgery with no __Host- cookie prefix and no
// CookieSecurePolicy.Always, so nothing here forces TLS -- see SampleProcessFixture's own
// remarks on the same point. No throwaway cert to mint or trust.

import { dirname, join } from "@std/path";

export const DEFAULT_PORT = 5997;

export interface StartOptions {
  repoRoot: string;
  port?: number;
  exePath?: string;
  env?: Record<string, string>;
}

export interface RunningApp {
  pid: number;
  baseUrl: string;
  /** The app's last few log lines, oldest first. Empty until it writes something. */
  logTail(): string;
  /** Its exit code once it has exited, null while it is still running. */
  exitCode(): number | null;
  stop(): Promise<void>;
}

/** How many of the app's log lines to keep for a failure message. */
export const LOG_TAIL_LINES = 40;

/** How long a killed app gets to exit before it is killed harder. */
export const KILL_GRACE_MS = 5_000;

/**
 * Throw if something is already listening on the port.
 *
 * A previous run killed at the wrong moment -- taskkill on the harness, an OOM, a wrapper
 * timeout -- leaves the published binary bound to 5997 with the page limiter raised. Without
 * this check the next run's `waitForHealthz` gets a 200 from that stale process and the harness
 * benchmarks last week's binary while reporting it as today's.
 */
export function assertPortFree(port: number, listen = Deno.listen): void {
  let listener: Deno.Listener;
  try {
    listener = listen({ port, hostname: "127.0.0.1" });
  } catch {
    throw new Error(
      `Port ${port} is already in use. A previous bench run may have left the app running -- `
        + `stop it, or pass --port <n>.`,
    );
  }
  listener.close();
}

/**
 * Where this harness publishes, relative to the repo root.
 *
 * Its own directory, and not the default output path, because the default is
 * `apps/sample/bin/Release/net10.0/<rid>/publish` -- which is the directory
 * `tests/nativeweb.scaffold.tests.e2e/SampleProcessFixture.cs` also publishes into and
 * launches from. A bench run interleaved with the e2e tier had the two writing one
 * directory, and the quieter half of that bug is `--skip-publish`: it would happily
 * benchmark whatever binary the e2e tier last put there and report the number as this
 * harness's own.
 *
 * Under `tmp/` rather than the repo-root `publish/`, which is deliberately NOT gitignored
 * (see .gitignore: justsmtp commits a top-level publish/, so this repo does not blanket-ignore
 * one). A throwaway NativeAOT publish landing somewhere `git add -A` can see it has cost this
 * repo a bad commit once already. `tmp/` is gitignored by rule, and the harness already writes
 * `tmp/bench/latest.json`.
 */
export const PUBLISH_DIR = join("tmp", "bench", "publish");

/**
 * Locate the published binary in the harness's own publish directory.
 *
 * No RID scan any more: with the output pinned there is exactly one, so "several RIDs, take
 * the newest" was answering a question this function can no longer be asked. What it must
 * still do is refuse to guess -- a `--skip-publish` run against a directory nobody has
 * published into should say so, not benchmark something else.
 */
export async function findPublishedExe(repoRoot: string): Promise<string> {
  const exeName = Deno.build.os === "windows" ? "nativeweb.sample.exe" : "nativeweb.sample";
  const candidate = join(repoRoot, PUBLISH_DIR, exeName);

  try {
    const info = await Deno.stat(candidate);
    if (info.isFile) return candidate;
  } catch {
    // Fall through to the one error message, below.
  }

  throw new Error(
    `No published ${exeName} at ${candidate}. Run the harness without --skip-publish, or `
      + `\`dotnet publish apps/sample -c Release -o ${PUBLISH_DIR}\` first. Note this is the `
      + `harness's own publish directory, not apps/sample/bin/Release/*/publish -- that one `
      + `belongs to the e2e fixture.`,
  );
}

export async function publish(repoRoot: string): Promise<void> {
  const csproj = join(repoRoot, "apps", "sample", "nativeweb.sample.csproj");
  const command = new Deno.Command("dotnet", {
    args: ["publish", csproj, "-c", "Release", "-o", PUBLISH_DIR, "-v", "q", "--nologo"],
    cwd: repoRoot,
    stdout: "inherit",
    stderr: "inherit",
  });
  const { code } = await command.output();
  if (code !== 0) throw new Error(`dotnet publish exited ${code}.`);
}

/**
 * Poll until the app answers /healthz with a 200. Throws with the elapsed wait on timeout --
 * a benchmark that hangs forever on a server that never came up tells you nothing.
 */
export async function waitForHealthz(
  baseUrl: string,
  timeoutMs: number,
  fetchImpl: typeof fetch = fetch,
  sleep: (ms: number) => Promise<void> = (ms) => new Promise((r) => setTimeout(r, ms)),
  now: () => number = () => performance.now(),
  // A process that died on startup would otherwise be waited on for the full minute and then
  // reported as "did not answer", which describes a hang rather than what happened.
  exitCode: () => number | null = () => null,
): Promise<number> {
  const start = now();
  while (now() - start < timeoutMs) {
    const dead = exitCode();
    if (dead !== null) {
      throw new Error(`The app exited with code ${dead} before answering ${baseUrl}/healthz.`);
    }

    try {
      const response = await fetchImpl(`${baseUrl}/healthz`);
      await response.arrayBuffer();
      if (response.ok) return now() - start;
    } catch {
      // Not listening yet.
    }
    await sleep(100);
  }
  throw new Error(`${baseUrl}/healthz did not answer within ${timeoutMs}ms.`);
}

/**
 * Environment for a benchmark launch.
 *
 * No rate-limit override: apps/sample has no page limiter to raise (grepped for RateLimit /
 * AddRateLimiter, neither appears). ConnectionStrings__Sample is left unset, same as
 * InMemorySampleFixture -- an unset connection string selects the in-memory store, so the
 * harness needs no database. Telemetry off: an OTLP exporter pointed at a collector that is
 * not there is background retry work on the process being measured.
 */
export function benchEnv(baseUrl: string): Record<string, string> {
  return {
    ASPNETCORE_URLS: baseUrl,
    // Development: /signin (the demo sign-in) is gated to it, same reason
    // SampleProcessFixture sets it. Not required for the two scenarios this harness drives --
    // /healthz and the unauthenticated /articles -- but keeping the launch environment the
    // same as the e2e fixture's is one fewer variable between what is measured and what is
    // tested.
    ASPNETCORE_ENVIRONMENT: "Development",
    // Deno.Command's env option adds to the inherited environment rather than replacing it,
    // so a harness invocation whose own shell exports ConnectionStrings__Sample (set for the
    // JauntyQ tiers, say) would otherwise hand it straight to the child. Empty, not absent, so
    // this always wins: string.IsNullOrWhiteSpace treats it the same as unset and Program.cs
    // selects the in-memory store, same as InMemorySampleFixture.
    ConnectionStrings__Sample: "",
    // Warning and above. RequestLoggingMiddleware writes an info line per request, and this
    // harness drains the app's output rather than discarding it (below), so at default level
    // the run measures the harness draining its own log: 8,020 req/s on `home` became 6,900,
    // a 14% cut, measured on apps/web before it left the repo. The cost of formatting an info
    // line per request is therefore NOT in these numbers; a production deployment at default
    // level pays it.
    Logging__LogLevel__Default: "Warning",
    NativeWeb__Telemetry__Enabled: "false",
  };
}

export async function startApp(options: StartOptions): Promise<RunningApp> {
  const port = options.port ?? DEFAULT_PORT;
  const baseUrl = `http://localhost:${port}`;
  assertPortFree(port);
  const exePath = options.exePath ?? await findPublishedExe(options.repoRoot);

  // From inside its own publish folder, as a deployment runs it: ContentRootPath-relative
  // lookups (wwwroot, appsettings.json) resolve against the CWD otherwise.
  const command = new Deno.Command(exePath, {
    cwd: dirname(exePath),
    env: { ...benchEnv(baseUrl), ...options.env },
    // Piped and drained into a bounded tail rather than discarded. A port already in use or a
    // startup guard refusing to boot are otherwise silent, and the only symptom is
    // waitForHealthz timing out sixty seconds later with no cause.
    stdout: "piped",
    stderr: "piped",
  });

  const child = command.spawn();
  const tail: string[] = [];
  let exitCode: number | null = null;
  const exited = child.status.then((status) => {
    exitCode = status.code;
    return status;
  });
  const drain = Promise.all([
    drainInto(child.stdout, tail),
    drainInto(child.stderr, tail),
  ]);

  return {
    pid: child.pid,
    baseUrl,
    logTail: () => tail.join("\n"),
    exitCode: () => exitCode,
    async stop(): Promise<void> {
      try {
        child.kill();
      } catch {
        // Already gone.
      }
      // SIGTERM first, SIGKILL if it has not gone in KILL_GRACE_MS. Without the escalation a
      // process wedged in the native runtime blocks this await forever, the harness never
      // exits, and the binary keeps the port -- the state the pre-flight check below exists to
      // catch on the NEXT run, which is one run too late.
      const escalate = setTimeout(() => {
        try {
          child.kill("SIGKILL");
        } catch {
          // Raced with a normal exit.
        }
      }, KILL_GRACE_MS);
      try {
        await exited;
        await drain;
      } finally {
        clearTimeout(escalate);
      }
    },
  };
}

/** Read a stream to exhaustion, keeping the last LOG_TAIL_LINES lines. */
export async function drainInto(
  stream: ReadableStream<Uint8Array>,
  tail: string[],
): Promise<void> {
  const decoder = new TextDecoder();
  let partial = "";

  try {
    for await (const chunk of stream) {
      partial += decoder.decode(chunk, { stream: true });
      const lines = partial.split("\n");
      partial = lines.pop() ?? "";
      for (const line of lines) {
        tail.push(line.trimEnd());
        if (tail.length > LOG_TAIL_LINES) tail.shift();
      }
    }
  } catch {
    // Killed mid-read; whatever is already in the tail is what there is.
  }
}
