import { assert, assertEquals, assertRejects, assertStringIncludes } from "@std/assert";

import { benchEnv, findPublishedExe, waitForHealthz } from "./server.ts";

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
  assert(elapsed >= 0);
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

Deno.test("the bench environment raises the page limiter, silences telemetry and serves TLS", () => {
  const cert = { certPath: "/tmp/c.pem", keyPath: "/tmp/k.pem", pem: "" };
  const env = benchEnv("https://localhost:5997", cert);

  assertEquals(env.ASPNETCORE_URLS, "https://localhost:5997");
  // Without a cert Kestrel would not serve https at all, and over http every rendered page
  // 500s on the Secure-always antiforgery cookie.
  assertEquals(env.ASPNETCORE_Kestrel__Certificates__Default__Path, "/tmp/c.pem");
  assertEquals(env.ASPNETCORE_Kestrel__Certificates__Default__KeyPath, "/tmp/k.pem");
  assertEquals(env.NativeWeb__Telemetry__Enabled, "false");
  // GET / is behind RequireRateLimiting("page"); at the shipped limit a load run measures
  // 429s rather than the server.
  assert(Number(env.NativeWeb__RateLimit__Page__PermitLimit) > 1_000_000);
});

Deno.test("findPublishedExe names the directory it looked in when nothing is published", async () => {
  const root = await Deno.makeTempDir();
  try {
    const error = await assertRejects(() => findPublishedExe(root), Error);
    assertStringIncludes(error.message, "Release");
    assertStringIncludes(error.message, "--skip-publish");
  } finally {
    await Deno.remove(root, { recursive: true });
  }
});

Deno.test("findPublishedExe finds the binary under an arbitrary RID directory", async () => {
  const root = await Deno.makeTempDir();
  try {
    const exeName = Deno.build.os === "windows" ? "nativeweb.web.exe" : "nativeweb.web";
    const dir = `${root}/apps/web/bin/Release/net10.0/some-rid-x64/publish`;
    await Deno.mkdir(dir, { recursive: true });
    await Deno.writeTextFile(`${dir}/${exeName}`, "");

    assertStringIncludes(await findPublishedExe(root), exeName);
  } finally {
    await Deno.remove(root, { recursive: true });
  }
});

Deno.test("a RID directory with no publish output is not mistaken for one", async () => {
  const root = await Deno.makeTempDir();
  try {
    await Deno.mkdir(`${root}/apps/web/bin/Release/net10.0/some-rid-x64`, { recursive: true });
    await assertRejects(() => findPublishedExe(root), Error);
  } finally {
    await Deno.remove(root, { recursive: true });
  }
});
