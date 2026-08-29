import { assert, assertEquals, assertRejects, assertStringIncludes } from "@std/assert";

import { CA_ARGS, CSR_ARGS, ensureCert, LEAF_EXTENSIONS, resolveOpenssl } from "./cert.ts";

Deno.test("resolveOpenssl takes the first candidate that answers", async () => {
  const tried: string[] = [];
  const found = await resolveOpenssl(["a", "b", "c"], (path) => {
    tried.push(path);
    return Promise.resolve(path === "b");
  });

  assertEquals(found, "b");
  // Stops at the first hit rather than probing every path.
  assertEquals(tried, ["a", "b"]);
});

Deno.test("resolveOpenssl names every path it tried when it finds none", async () => {
  const error = await assertRejects(
    () => resolveOpenssl(["/nowhere/openssl", "/also/nowhere"], () => Promise.resolve(false)),
    Error,
  );

  assertStringIncludes(error.message, "/nowhere/openssl");
  assertStringIncludes(error.message, "/also/nowhere");
});

Deno.test("the leaf carries both loopback spellings and is not a CA", () => {
  // The harness fetches https://localhost; a leaf without the DNS name fails the client's
  // hostname check and every request in the run becomes a transport error. CA:FALSE is the
  // other half: Deno refuses a trust anchor offered as an end entity outright.
  assertStringIncludes(LEAF_EXTENSIONS, "DNS:localhost");
  assertStringIncludes(LEAF_EXTENSIONS, "IP:127.0.0.1");
  assertStringIncludes(LEAF_EXTENSIONS, "CA:FALSE");
});

Deno.test("the CA is a CA and the leaf's key needs no passphrase", () => {
  const ca = CA_ARGS("ca-key.pem", "ca.pem", 30);
  assertStringIncludes(ca[ca.indexOf("-addext") + 1], "CA:TRUE");
  assertEquals(ca[ca.indexOf("-days") + 1], "30");

  // No passphrase anywhere: Kestrel's PEM KeyPath cannot supply one.
  assert(ca.includes("-nodes"));
  assert(CSR_ARGS("k.pem", "r.csr").includes("-nodes"));
});

Deno.test("ensureCert mints a usable pair and reuses it on a second call", async () => {
  const dir = await Deno.makeTempDir();
  try {
    const first = await ensureCert(dir, 2);
    assertStringIncludes(first.pem, "BEGIN CERTIFICATE");
    assert((await Deno.readTextFile(first.keyPath)).includes("PRIVATE KEY"));

    const mintedAt = (await Deno.stat(first.certPath)).mtime?.getTime();
    const second = await ensureCert(dir, 2);

    assertEquals(second.certPath, first.certPath);
    assertEquals((await Deno.stat(second.certPath)).mtime?.getTime(), mintedAt);
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("a minted certificate actually serves TLS that the driver's client trusts", async () => {
  const dir = await Deno.makeTempDir();
  try {
    const cert = await ensureCert(dir, 2);
    const controller = new AbortController();
    const server = Deno.serve({
      port: 0,
      cert: await Deno.readTextFile(cert.certPath),
      key: await Deno.readTextFile(cert.keyPath),
      signal: controller.signal,
      onListen: () => {},
    }, () => new Response("ok"));

    try {
      const client = Deno.createHttpClient({ caCerts: [cert.pem] });
      try {
        const response = await fetch(
          `https://localhost:${server.addr.port}/`,
          { client } as unknown as RequestInit,
        );
        assertEquals(await response.text(), "ok");
      } finally {
        client.close();
      }
    } finally {
      controller.abort();
      await server.finished;
    }
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});
