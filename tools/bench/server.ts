// Publish apps/web, launch the published binary, wait for it to answer, stop it again.
//
// The published Release output, not `dotnet run`: that is the binary a deployment runs, and
// it is the only one whose figures mean anything -- a Debug host is a different JIT, a
// different static-file provider and a different startup path. Same choice, for overlapping
// reasons, as tests/nativeweb.web.tests.e2e/AppProcessFixture.cs.
//
// https on loopback, with a throwaway cert from cert.ts, because http is not an option: the
// antiforgery cookie is Secure-always and every rendered page throws a 500 over http. Measured
// on the published binary -- see the comment at the top of cert.ts. The TLS cost is paid once
// per connection and the driver keeps connections alive, so it does not dominate the numbers,
// and production serves https anyway.

import { join } from "@std/path";

import { type Cert, ensureCert } from "./cert.ts";

export const DEFAULT_PORT = 5997;

export interface StartOptions {
  repoRoot: string;
  port?: number;
  exePath?: string;
  cert?: Cert;
  env?: Record<string, string>;
}

export interface RunningApp {
  pid: number;
  baseUrl: string;
  /** The certificate it is listening with, for the driver's caCerts. */
  cert: Cert;
  stop(): Promise<void>;
}

/**
 * Locate the published binary. Scans for it rather than computing the RID: a checkout can
 * carry publish output for more than one RID, and a stale guess produces "file not found"
 * where the real problem is "you have not published".
 */
export async function findPublishedExe(repoRoot: string): Promise<string> {
  const exeName = Deno.build.os === "windows" ? "nativeweb.web.exe" : "nativeweb.web";
  const releaseRoot = join(repoRoot, "apps", "web", "bin", "Release", "net10.0");

  const found: string[] = [];
  try {
    for await (const entry of Deno.readDir(releaseRoot)) {
      if (!entry.isDirectory) continue;
      const candidate = join(releaseRoot, entry.name, "publish", exeName);
      try {
        const info = await Deno.stat(candidate);
        if (info.isFile) found.push(candidate);
      } catch {
        // No publish output for this RID; keep looking.
      }
    }
  } catch {
    throw new Error(
      `No Release output under ${releaseRoot}. Run the harness without --skip-publish, or `
        + `\`dotnet publish apps/web -c Release\` first.`,
    );
  }

  if (found.length === 0) {
    throw new Error(
      `No published ${exeName} under ${releaseRoot}/*/publish. Run the harness without `
        + `--skip-publish.`,
    );
  }

  // More than one RID published: newest wins, and say so, because benchmarking last month's
  // binary and reporting it as today's is the failure this whole harness exists to prevent.
  if (found.length > 1) {
    const stats = await Promise.all(
      found.map(async (path) => ({ path, mtime: (await Deno.stat(path)).mtime?.getTime() ?? 0 })),
    );
    stats.sort((a, b) => b.mtime - a.mtime);
    console.log(`  note: ${found.length} published RIDs found; using the newest, ${stats[0].path}`);
    return stats[0].path;
  }

  return found[0];
}

export async function publish(repoRoot: string): Promise<void> {
  const csproj = join(repoRoot, "apps", "web", "nativeweb.web.csproj");
  const command = new Deno.Command("dotnet", {
    args: ["publish", csproj, "-c", "Release", "-v", "q", "--nologo"],
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
): Promise<number> {
  const start = now();
  while (now() - start < timeoutMs) {
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
 * The page rate limiter is the one knob that has to move: GET / is behind
 * RequireRateLimiting("page") (apps/web/Program.cs:831) at a limit sized for one human per
 * minute, so a load run would measure 429s. Raised the same way AppProcessFixture raises the
 * auth limiters for the same reason. Telemetry off: an OTLP exporter pointed at a collector
 * that is not there is background retry work on the process being measured.
 */
export function benchEnv(baseUrl: string, cert: Cert): Record<string, string> {
  return {
    ASPNETCORE_URLS: baseUrl,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_Kestrel__Certificates__Default__Path: cert.certPath,
    ASPNETCORE_Kestrel__Certificates__Default__KeyPath: cert.keyPath,
    NativeWeb__Telemetry__Enabled: "false",
    NativeWeb__RateLimit__Page__PermitLimit: "1000000000",
  };
}

export async function startApp(options: StartOptions): Promise<RunningApp> {
  const port = options.port ?? DEFAULT_PORT;
  // "localhost", not 127.0.0.1: the cert's SAN carries both, but a name the client can verify
  // keeps the TLS handshake off the list of things a failed run could be blamed on.
  const baseUrl = `https://localhost:${port}`;
  const exePath = options.exePath ?? await findPublishedExe(options.repoRoot);
  const cert = options.cert ?? await ensureCert(join(options.repoRoot, "tmp", "bench"));

  // From inside its own publish folder, as a deployment runs it: ContentRootPath-relative
  // lookups (wwwroot, appsettings.json) resolve against the CWD otherwise.
  const command = new Deno.Command(exePath, {
    cwd: exePath.slice(0, Math.max(exePath.lastIndexOf("/"), exePath.lastIndexOf("\\"))),
    env: { ...benchEnv(baseUrl, cert), ...options.env },
    stdout: "null",
    stderr: "null",
  });

  const child = command.spawn();

  return {
    pid: child.pid,
    baseUrl,
    cert,
    async stop(): Promise<void> {
      try {
        child.kill();
      } catch {
        // Already gone.
      }
      await child.status;
    },
  };
}
