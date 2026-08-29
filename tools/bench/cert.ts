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

/**
 * Mint a CA and a leaf into `dir`, reusing what is already there. Reuse is by presence, not by
 * expiry: the lifetime is long enough that a stale pair means the directory survived years,
 * and `tmp/` does not.
 */
export async function ensureCert(dir: string, days = 365): Promise<Cert> {
  const caCertPath = join(dir, "bench-ca.pem");
  const caKeyPath = join(dir, "bench-ca-key.pem");
  const certPath = join(dir, "bench-cert.pem");
  const keyPath = join(dir, "bench-key.pem");

  await Deno.mkdir(dir, { recursive: true });

  try {
    const pem = await Deno.readTextFile(caCertPath);
    await Deno.stat(certPath);
    await Deno.stat(keyPath);
    return { certPath, keyPath, pem };
  } catch {
    // Not minted yet.
  }

  const openssl = await resolveOpenssl();
  const csrPath = join(dir, "bench.csr");
  const extPath = join(dir, "bench-leaf.ext");
  await Deno.writeTextFile(extPath, `${LEAF_EXTENSIONS}\n`);

  await run(openssl, CA_ARGS(caKeyPath, caCertPath, days));
  await run(openssl, CSR_ARGS(keyPath, csrPath));
  await run(openssl, SIGN_ARGS(csrPath, caCertPath, caKeyPath, extPath, certPath, days));

  return { certPath, keyPath, pem: await Deno.readTextFile(caCertPath) };
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
