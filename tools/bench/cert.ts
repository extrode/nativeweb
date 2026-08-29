// A throwaway certificate chain for the benchmark's loopback listener.
//
// The harness cannot measure GET / over http. The antiforgery cookie is
// `__Host-nw-antiforgery` with `CookieSecurePolicy.Always` (apps/web/Program.cs:462), pinned
// unconditionally on purpose, and `Layout.Page` mints a token on every rendered page -- so
// over http every page render throws `The antiforgery system has the configuration value
// AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an SSL
// request` and answers 500. Measured on the published binary before this file existed: three
// runs of GET /, 245,106 responses, every one of them a 500.
// tests/nativeweb.web.tests.e2e/AppProcessFixture.cs mints its own cert for an overlapping
// reason (the __Host- session cookie), and this is the same choice one runtime over.
//
// A CA and a leaf, not one self-signed certificate. Deno's TLS stack rejects a self-signed
// cert offered as a leaf even when it is the configured trust anchor -- `invalid peer
// certificate: CaUsedAsEndEntity`, measured, which is why the two-step dance below exists.
// Kestrel gets the leaf; the driver trusts the CA.
//
// PEM rather than PFX because Kestrel takes a Path/KeyPath pair directly and openssl emits
// that with no password step. Everything lives under the repo's own tmp/, which is gitignored.

import { join } from "@std/path";

/** Where openssl might be, in order. Git for Windows ships one; PATH is tried first. */
export const OPENSSL_CANDIDATES = [
  "openssl",
  "C:\\Program Files\\Git\\mingw64\\bin\\openssl.exe",
  "C:\\Program Files\\Git\\usr\\bin\\openssl.exe",
  "/usr/bin/openssl",
  "/opt/homebrew/bin/openssl",
];

export interface Cert {
  /** The leaf, for Kestrel. */
  certPath: string;
  keyPath: string;
  /** The CA in PEM form, for Deno.createHttpClient's caCerts. */
  pem: string;
}

/**
 * First openssl that answers `version`. Throws naming every path tried -- "openssl not found"
 * with no list is the kind of error that costs an hour on a machine where it is installed
 * somewhere unusual.
 */
export async function resolveOpenssl(
  candidates: string[] = OPENSSL_CANDIDATES,
  probe: (path: string) => Promise<boolean> = defaultProbe,
): Promise<string> {
  for (const candidate of candidates) {
    if (await probe(candidate)) return candidate;
  }

  throw new Error(
    `The benchmark needs openssl to mint a loopback certificate (apps/web refuses to render a `
      + `page over http) and found none. Tried: ${candidates.join(", ")}.`,
  );
}

async function defaultProbe(path: string): Promise<boolean> {
  try {
    const { code } = await new Deno.Command(path, {
      args: ["version"],
      stdout: "null",
      stderr: "null",
    }).output();
    return code === 0;
  } catch {
    return false;
  }
}

/**
 * The leaf's extensions. `CA:FALSE` is the half that matters: with `CA:TRUE` the certificate
 * is a trust anchor being used as an end entity, which Deno's client refuses outright.
 * subjectAltName carries both loopback spellings, because the client checks the name.
 */
export const LEAF_EXTENSIONS = [
  "basicConstraints=critical,CA:FALSE",
  "keyUsage=critical,digitalSignature,keyEncipherment",
  "extendedKeyUsage=serverAuth",
  "subjectAltName=DNS:localhost,IP:127.0.0.1",
].join("\n");

export const CA_ARGS = (keyPath: string, certPath: string, days: number): string[] => [
  "req",
  "-x509",
  "-newkey",
  "rsa:2048",
  "-nodes",
  "-keyout",
  keyPath,
  "-out",
  certPath,
  "-days",
  String(days),
  "-subj",
  "/CN=NativeWeb bench CA",
  "-addext",
  "basicConstraints=critical,CA:TRUE",
];

export const CSR_ARGS = (keyPath: string, csrPath: string): string[] => [
  "req",
  "-new",
  "-newkey",
  "rsa:2048",
  "-nodes",
  "-keyout",
  keyPath,
  "-out",
  csrPath,
  "-subj",
  "/CN=localhost",
];

export const SIGN_ARGS = (
  csrPath: string,
  caCertPath: string,
  caKeyPath: string,
  extPath: string,
  certPath: string,
  days: number,
): string[] => [
  "x509",
  "-req",
  "-in",
  csrPath,
  "-CA",
  caCertPath,
  "-CAkey",
  caKeyPath,
  "-CAcreateserial",
  "-out",
  certPath,
  "-days",
  String(days),
  "-extfile",
  extPath,
];

/** Every file of the pair exists, the leaf is still valid tomorrow, and it chains to the CA. */
export async function reusable(
  paths: { caCertPath: string; caKeyPath: string; certPath: string; keyPath: string },
  openssl: string,
  check: (openssl: string, args: string[]) => Promise<boolean> = exitZero,
): Promise<boolean> {
  for (const path of Object.values(paths)) {
    try {
      await Deno.stat(path);
    } catch {
      return false;
    }
  }

  // Presence alone was the first rule, and it rots: a year on, the reuse branch would keep
  // handing back an expired leaf, every request would fail the handshake, and the only symptom
  // would be a run full of TLS errors with nothing naming the cert.
  const unexpired = await check(
    openssl,
    ["x509", "-checkend", "86400", "-noout", "-in", paths.certPath],
  );
  if (!unexpired) return false;

  // Expiry was not enough either. Four files all present does not mean they belong together:
  // a run interrupted between minting the CA and signing the leaf leaves a NEW ca beside the
  // PREVIOUS leaf, both unexpired, and every later run reuses that set. Kestrel then serves a
  // leaf the driver's trust anchor never signed, and every request fails the handshake as a
  // generic transport error -- for up to a year, until the leaf's own -checkend finally fires.
  // Minting now happens in scratch and moves into place (see ensureCert), so this is the
  // second lock rather than the only one.
  return await check(openssl, ["verify", "-CAfile", paths.caCertPath, paths.certPath]);
}

/**
 * Mint a CA and a leaf into `dir`, reusing what is already there when the pair is complete and
 * the leaf is not about to expire.
 */
export async function ensureCert(dir: string, days = 365): Promise<Cert> {
  const caCertPath = join(dir, "bench-ca.pem");
  const caKeyPath = join(dir, "bench-ca-key.pem");
  const certPath = join(dir, "bench-cert.pem");
  const keyPath = join(dir, "bench-key.pem");

  await Deno.mkdir(dir, { recursive: true });

  const openssl = await resolveOpenssl();

  if (await reusable({ caCertPath, caKeyPath, certPath, keyPath }, openssl)) {
    return { certPath, keyPath, pem: await Deno.readTextFile(caCertPath) };
  }

  // Mint into scratch, then move into place. Minting is three openssl runs that used to write
  // the live files directly, so a Ctrl-C between the first and the third left a new CA next to
  // the old leaf -- a mixed set that every later run happily reused. Here an interrupted mint
  // leaves the previous set untouched and the scratch directory orphaned under tmp/.
  const scratch = join(dir, "mint");
  await Deno.mkdir(scratch, { recursive: true });

  const stagedCaCert = join(scratch, "bench-ca.pem");
  const stagedCaKey = join(scratch, "bench-ca-key.pem");
  const stagedCert = join(scratch, "bench-cert.pem");
  const stagedKey = join(scratch, "bench-key.pem");
  const csrPath = join(scratch, "bench.csr");
  const extPath = join(scratch, "bench-leaf.ext");
  await Deno.writeTextFile(extPath, `${LEAF_EXTENSIONS}\n`);

  await run(openssl, CA_ARGS(stagedCaKey, stagedCaCert, days));
  await run(openssl, CSR_ARGS(stagedKey, csrPath));
  await run(openssl, SIGN_ARGS(csrPath, stagedCaCert, stagedCaKey, extPath, stagedCert, days));

  const staged: [string, string][] = [
    [stagedCaCert, caCertPath],
    [stagedCaKey, caKeyPath],
    [stagedCert, certPath],
    [stagedKey, keyPath],
  ];
  for (const [from, to] of staged) {
    await Deno.rename(from, to);
  }

  await Deno.remove(scratch, { recursive: true }).catch(() => {});

  return { certPath, keyPath, pem: await Deno.readTextFile(caCertPath) };
}

/** Run an openssl command for its exit code alone -- `-checkend` and `verify` both answer that way. */
async function exitZero(openssl: string, args: string[]): Promise<boolean> {
  try {
    const { code } = await new Deno.Command(openssl, { args, stdout: "null", stderr: "null" })
      .output();
    return code === 0;
  } catch {
    return false;
  }
}

async function run(openssl: string, args: string[]): Promise<void> {
  const { code, stderr } = await new Deno.Command(openssl, {
    args,
    stdout: "null",
    stderr: "piped",
  }).output();

  if (code !== 0) {
    throw new Error(
      `openssl ${args[0]} exited ${code}: ${new TextDecoder().decode(stderr).trim()}`,
    );
  }
}
