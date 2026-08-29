import { assert, assertEquals, assertStringIncludes } from "@std/assert";
import { computeId, type Candidate } from "./ledger.ts";
import { redactCandidate, redactText, scanCorpus, scanText } from "./redact.ts";

function candidate(over: Partial<Candidate> = {}): Candidate {
  return {
    ruleId: "hardcoded-secret",
    path: "apps/web/Db.cs",
    line: 41,
    what: "A password is hard-coded in the connection string.",
    why: "It ships in the binary and cannot be rotated without a release.",
    confirm: "Open apps/web/Db.cs at line 41.",
    excerpt: "var cs = \"Host=db;Username=app;Password=hunter2correct;Database=nw\";",
    verified: "verified",
    ...over,
  };
}

// --- each rule finds its own shape ---------------------------------------------------------

const SPECIMENS: Array<[string, string, string]> = [
  ["aws-access-key-id", "key AKIAIOSFODNN7EXAMPLE here", "AKIAIOSFODNN7EXAMPLE"],
  ["github-token", "tok ghp_abcdefghijklmnopqrstuvwxyz0123456789 end", "ghp_abcdefghijklmnopqrstuvwxyz0123456789"],
  ["github-pat", "github_pat_11ABCDEFG0abcdefghij_KLMNOPqrstuv", "github_pat_11ABCDEFG0abcdefghij_KLMNOPqrstuv"],
  ["slack-token", "xoxb-1234567890-abcdefghij", "xoxb-1234567890-abcdefghij"],
  ["google-api-key", "AIzaSyA1234567890abcdefghijklmnopqrstuv", "AIzaSyA1234567890abcdefghijklmnopqrstuv"],
  ["openai-style-key", "sk-proj-abcdefghijklmnopqrstuvwxyz01", "sk-proj-abcdefghijklmnopqrstuvwxyz01"],
  ["jwt", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U", "eyJhbGciOiJIUzI1NiJ9"],
  ["pem-private-key", "-----BEGIN RSA PRIVATE KEY-----\nMIIEow\n-----END RSA PRIVATE KEY-----", "MIIEow"],
];

Deno.test("every named rule finds its own specimen", () => {
  for (const [kind, text] of SPECIMENS) {
    const kinds = scanText(text).map((m) => m.kind);
    assert(kinds.includes(kind), `${kind} not found in specimen; got ${JSON.stringify(kinds)}`);
  }
});

Deno.test("redaction removes the secret and keeps everything around it", () => {
  for (const [kind, text, secretFragment] of SPECIMENS) {
    const out = redactText(text);
    assert(!out.includes(secretFragment), `${kind}: secret survived redaction`);
    assertStringIncludes(out, `[redacted:${kind}]`);
  }
});

Deno.test("a connection-string password goes and the host stays", () => {
  const out = redactText("Host=db.internal;Username=app;Password=hunter2correct;Database=nw");
  assert(!out.includes("hunter2correct"));
  assertStringIncludes(out, "Host=db.internal");
  assertStringIncludes(out, "Database=nw");
});

Deno.test("credentials embedded in a URL are removed but the host is kept", () => {
  const out = redactText("postgres://appuser:s3cr3tPassphrase@db.internal:5432/nw");
  assert(!out.includes("s3cr3tPassphrase"));
  assertStringIncludes(out, "db.internal:5432/nw");
});

Deno.test("an assignment whose name says secret is redacted whatever the value looks like", () => {
  const out = redactText('const CLIENT_SECRET = "Zq81mVn0PpLdRt42";');
  assert(!out.includes("Zq81mVn0PpLdRt42"));
  assertStringIncludes(out, "CLIENT_SECRET");
});

Deno.test("a bearer token is removed from a curl line that stays runnable in shape", () => {
  const out = redactText("curl -H 'Authorization: Bearer abcdefghijklmnopqrstuvwxyz012345' /api");
  assert(!out.includes("abcdefghijklmnopqrstuvwxyz012345"));
  assertStringIncludes(out, "Authorization: Bearer");
  assertStringIncludes(out, "/api");
});

// --- what must NOT be redacted --------------------------------------------------------------

Deno.test("placeholders survive, so documentation examples stay readable", () => {
  const kept = [
    'PASSWORD="changeme"',
    "API_KEY=<your-key-here>",
    "CLIENT_SECRET=${CLIENT_SECRET}",
    "GITHUB_TOKEN=xxxxxxxxxxxx",
    "DB_PASSWORD=[REDACTED]",
    "ACCESS_KEY=your-access-key",
  ];
  for (const text of kept) assertEquals(redactText(text), text, `should not redact: ${text}`);
});

Deno.test("ordinary prose and code are untouched", () => {
  const text = "The handler returns 401 when the session cookie is absent; see SessionTests.cs:88.";
  assertEquals(redactText(text), text);
});

Deno.test("redacting twice changes nothing the second time", () => {
  const once = redactText("Password=hunter2correct;");
  assertEquals(redactText(once), once);
});

Deno.test("overlapping rules produce one marker, not nested ones", () => {
  const out = redactText("API_TOKEN=ghp_abcdefghijklmnopqrstuvwxyz0123456789");
  assertEquals(out.match(/\[redacted:/g)?.length, 1);
});

// --- the write path (R13) ---------------------------------------------------------------------

Deno.test("a candidate about a secret keeps its location and loses its value", () => {
  const { candidate: safe, redacted } = redactCandidate(candidate());
  assertEquals(safe.path, "apps/web/Db.cs");
  assertEquals(safe.line, 41);
  assert(!safe.excerpt.includes("hunter2correct"));
  assertStringIncludes(safe.excerpt, "Host=db");
  assertEquals(safe.what, candidate().what);
  assertEquals(redacted.map((r) => r.field), ["excerpt"]);
  assertEquals(redacted[0].kind, "connection-string-password");
});

Deno.test("redaction is applied to every reader-facing field, not only the excerpt", () => {
  const { candidate: safe } = redactCandidate(
    candidate({ confirm: "run: psql 'postgres://u:RealPassw0rdHere@db/nw'" }),
  );
  assert(!safe.confirm.includes("RealPassw0rdHere"));
});

Deno.test("identity is computed over the redacted excerpt, so it is stable across rule changes", async () => {
  const raw = candidate();
  const { candidate: safe } = redactCandidate(raw);
  // Redacting an already-redacted candidate must not move the id: this is the property that
  // stops a new pattern in November renaming every finding written in September.
  const { candidate: twice } = redactCandidate(safe);
  assertEquals(await computeId(safe), await computeId(twice));
  // And the raw form must hash differently, proving redaction happens before hashing matters.
  assert(await computeId(raw) !== await computeId(safe));
});

Deno.test("a clean candidate passes through unchanged", () => {
  const clean = candidate({ excerpt: "return Results.Unauthorized();", what: "No 401 body." });
  const { candidate: out, redacted } = redactCandidate(clean);
  assertEquals(redacted, []);
  assertEquals(out, clean);
});

// --- the corpus assertion (R13) -----------------------------------------------------------------

Deno.test("scanCorpus finds a planted secret and names its file and line", () => {
  const found = scanCorpus([
    { path: "reports/2026-08-30.md", text: "fine\nalso fine\n" },
    { path: "ledger/nativeweb.jsonl", text: "ok\nkey AKIAIOSFODNN7EXAMPLE\nok\n" },
  ]);
  assertEquals(found.length, 1);
  assertEquals(found[0].file, "ledger/nativeweb.jsonl");
  assertEquals(found[0].line, 2);
  assertEquals(found[0].kind, "aws-access-key-id");
});

Deno.test("scanCorpus is empty over a clean corpus", () => {
  assertEquals(scanCorpus([{ path: "a.md", text: "nothing here\nnor here\n" }]), []);
});

Deno.test("scanCorpus reports locations and never the value", () => {
  const found = scanCorpus([{ path: "a.md", text: "Password=hunter2correct;" }]);
  assertEquals(found.length, 1);
  assert(!JSON.stringify(found).includes("hunter2correct"));
});
