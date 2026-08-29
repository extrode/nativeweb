import { assert, assertEquals, assertRejects, assertStringIncludes } from "@std/assert";

import {
  CA_ARGS,
  CSR_ARGS,
  ensureCert,
  LEAF_EXTENSIONS,
  resolveOpenssl,
  reusable,
} from "./cert.ts";
import { removeScratch, scratchDir } from "./scratch.ts";

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
  const dir = await scratchDir();
  try {
    const first = await ensureCert(dir, 2);
    assertStringIncludes(first.pem, "BEGIN CERTIFICATE");
    assert((await Deno.readTextFile(first.keyPath)).includes("PRIVATE KEY"));

    const mintedAt = (await Deno.stat(first.certPath)).mtime?.getTime();
    const second = await ensureCert(dir, 2);

    assertEquals(second.certPath, first.certPath);
    assertEquals((await Deno.stat(second.certPath)).mtime?.getTime(), mintedAt);
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("a minted certificate actually serves TLS that the driver's client trusts", async () => {
  const dir = await scratchDir();
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
    await removeScratch(dir);
  }
});

Deno.test("an expired leaf is not reused, however present the files are", async () => {
  const dir = await scratchDir();
  try {
    for (const name of ["bench-ca.pem", "bench-ca-key.pem", "bench-cert.pem", "bench-key.pem"]) {
      await Deno.writeTextFile(`${dir}/${name}`, "not a real cert");
    }
    const paths = {
      caCertPath: `${dir}/bench-ca.pem`,
      caKeyPath: `${dir}/bench-ca-key.pem`,
      certPath: `${dir}/bench-cert.pem`,
      keyPath: `${dir}/bench-key.pem`,
    };

    // Presence alone was the original rule. A year on it would keep handing back a dead leaf,
    // and every request would fail the handshake with nothing naming the cert.
    assertEquals(await reusable(paths, "openssl", () => Promise.resolve(false)), false);
    assertEquals(await reusable(paths, "openssl", () => Promise.resolve(true)), true);
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("a half-minted pair is not reused, and the expiry is not even consulted", async () => {
  const dir = await scratchDir();
  try {
    // The CA key was the file the presence check forgot: without it the leaf cannot be
    // re-signed, so a directory missing it is not reusable however valid the leaf looks.
    await Deno.writeTextFile(`${dir}/bench-ca.pem`, "x");
    await Deno.writeTextFile(`${dir}/bench-cert.pem`, "x");
    await Deno.writeTextFile(`${dir}/bench-key.pem`, "x");

    let checked = false;
    const ok = await reusable(
      {
        caCertPath: `${dir}/bench-ca.pem`,
        caKeyPath: `${dir}/bench-ca-key.pem`,
        certPath: `${dir}/bench-cert.pem`,
        keyPath: `${dir}/bench-key.pem`,
      },
      "openssl",
      () => {
        checked = true;
        return Promise.resolve(true);
      },
    );

    assertEquals(ok, false);
    assertEquals(checked, false);
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("a real minted pair is reusable, expiry check and all", async () => {
  const dir = await scratchDir();
  try {
    await ensureCert(dir, 2);
    assertEquals(await reusable(paths(dir), await resolveOpenssl()), true);
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("a leaf the CA on disk did not sign is refused, however unexpired both are", async () => {
  const dir = await scratchDir();
  const other = await scratchDir();
  try {
    const openssl = await resolveOpenssl();
    await ensureCert(dir, 365);
    await ensureCert(other, 365);

    // What an interrupted mint used to leave behind: a fresh CA beside the previous leaf. Both
    // files are present, both are valid for a year, and every check the old rule made passes --
    // while Kestrel serves a certificate the driver's trust anchor never signed and every
    // request in the run dies as an unexplained transport error.
    await Deno.copyFile(`${other}/bench-ca.pem`, `${dir}/bench-ca.pem`);
    await Deno.copyFile(`${other}/bench-ca-key.pem`, `${dir}/bench-ca-key.pem`);

    assertEquals(await reusable(paths(dir), openssl), false);
  } finally {
    await removeScratch(dir);
    await removeScratch(other);
  }
});

Deno.test("a leaf inside the expiry window is reminted, not handed back", async () => {
  const dir = await scratchDir();
  try {
    const openssl = await resolveOpenssl();

    // A one-day leaf has slightly under 86,400 seconds left the moment it is written, so
    // `-checkend 86400` refuses it. This is the remint branch that would otherwise fire for the
    // first time a year after the baseline was recorded, on someone else's run.
    await ensureCert(dir, 1);
    const expiring = await Deno.readTextFile(`${dir}/bench-cert.pem`);
    assertEquals(await reusable(paths(dir), openssl), false);

    await ensureCert(dir, 365);
    const renewed = await Deno.readTextFile(`${dir}/bench-cert.pem`);

    assert(renewed !== expiring, "ensureCert handed back the expiring leaf instead of minting one");
    assertEquals(await reusable(paths(dir), openssl), true);
  } finally {
    await removeScratch(dir);
  }
});

Deno.test("an interrupted mint leaves the previous pair intact, not a mixture", async () => {
  const dir = await scratchDir();
  try {
    const openssl = await resolveOpenssl();
    await ensureCert(dir, 365);
    const before = await Deno.readTextFile(`${dir}/bench-cert.pem`);

    // The four live files are the only things minting may write into dir: the CSR and the
    // extension file are intermediates, and their absence here is what proves the three openssl
    // runs happened in scratch rather than over the live pair. Write them straight into dir
    // again and a Ctrl-C between the first and the third leaves a new CA beside the old leaf.
    for (const intermediate of ["bench.csr", "bench-leaf.ext", "mint"]) {
      await assertRejects(
        () => Deno.stat(`${dir}/${intermediate}`),
        Deno.errors.NotFound,
        undefined,
        `${intermediate} was left in the live directory; minting did not stage`,
      );
    }

    // And a half-written scratch directory left by a killed run does not disturb the live pair.
    await Deno.mkdir(`${dir}/mint`, { recursive: true });
    await Deno.writeTextFile(`${dir}/mint/bench-ca.pem`, "half-written");

    assertEquals(await reusable(paths(dir), openssl), true);
    assertEquals(await Deno.readTextFile(`${dir}/bench-cert.pem`), before);
  } finally {
    await removeScratch(dir);
  }
});

function paths(dir: string) {
  return {
    caCertPath: `${dir}/bench-ca.pem`,
    caKeyPath: `${dir}/bench-ca-key.pem`,
    certPath: `${dir}/bench-cert.pem`,
    keyPath: `${dir}/bench-key.pem`,
  };
}
