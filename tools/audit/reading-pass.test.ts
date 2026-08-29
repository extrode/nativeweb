import { assert, assertEquals, assertRejects, assertStringIncludes } from "@std/assert";
import { complete, extractJsonArray, isRetryable, loadApiKey, ModelError } from "./model.ts";
import {
  buildReaderPrompt,
  buildRefuterPrompt,
  parseVerdict,
  readingPass,
  sliceBytes,
  toCandidates,
} from "./reading-pass.ts";
import type { SourceFile } from "./checks/types.ts";
import { validate } from "./ledger.ts";

const FILE: SourceFile = {
  path: "libs/a/Tokens.cs",
  text: ["class Tokens {", "  string? Read() {", "    return null;", "  }", "}"].join("\n"),
};

/** A transport that answers from a queue and records what it was asked. */
function stub(bodies: string[], status = 200) {
  const seen: Array<{ url: string; headers: Record<string, string>; body: unknown }> = [];
  const transport = async (request: Request) => {
    seen.push({
      url: request.url,
      headers: Object.fromEntries(request.headers),
      body: JSON.parse(await request.clone().text()),
    });
    const next = bodies.shift() ?? "[]";
    if (status !== 200) return new Response(next, { status });
    return new Response(
      JSON.stringify({
        content: [{ type: "text", text: next }],
        usage: { input_tokens: 10, output_tokens: 5 },
        stop_reason: "end_turn",
      }),
      { status: 200, headers: { "content-type": "application/json" } },
    );
  };
  return { transport, seen };
}

const OPTS = { apiKey: "k", attempts: 1, sleep: () => Promise.resolve() };

// --- the client -------------------------------------------------------------------------------

Deno.test("a completion posts to /v1/messages with the Anthropic headers", async () => {
  const { transport, seen } = stub(["hello"]);
  const result = await complete("sys", "user", { ...OPTS, transport });
  assertEquals(result.text, "hello");
  assertEquals(result.usage, { inputTokens: 10, outputTokens: 5 });
  assertEquals(seen.length, 1);
  assertStringIncludes(seen[0].url, "https://api.z.ai/api/anthropic/v1/messages");
  assertEquals(seen[0].headers["x-api-key"], "k");
  assertEquals(seen[0].headers["anthropic-version"], "2023-06-01");
  assertEquals((seen[0].body as { model: string }).model, "glm-5.3");
});

Deno.test("a 401 is not retried and surfaces as an error", async () => {
  const { transport, seen } = stub(["nope"], 401);
  await assertRejects(
    () => complete("s", "u", { ...OPTS, attempts: 3, transport }),
    ModelError,
  );
  assertEquals(seen.length, 1, "an auth failure must not be retried");
});

Deno.test("a 429 is retried up to the attempt limit", async () => {
  const { transport, seen } = stub(["busy", "busy", "busy"], 429);
  await assertRejects(() => complete("s", "u", { ...OPTS, attempts: 3, transport }), ModelError);
  assertEquals(seen.length, 3);
});

Deno.test("retryable statuses are the transient ones only", () => {
  for (const s of [408, 429, 500, 502, 503, 504]) assert(isRetryable(s), `${s} should retry`);
  for (const s of [400, 401, 403, 404, 422]) assert(!isRetryable(s), `${s} should not retry`);
});

Deno.test("a truncated answer is reported as truncated", async () => {
  const transport = () =>
    Promise.resolve(
      new Response(
        JSON.stringify({ content: [{ type: "text", text: "[" }], stop_reason: "max_tokens" }),
        { status: 200 },
      ),
    );
  assertEquals((await complete("s", "u", { ...OPTS, transport })).truncated, true);
});

Deno.test("loadApiKey prefers the environment", async () => {
  assertEquals(await loadApiKey((k) => (k === "ZAI_API_KEY" ? " abc " : undefined)), "abc");
});

Deno.test("loadApiKey reads a bare key or an assignment from ~/.zai", async () => {
  const dir = await Deno.makeTempDir({ prefix: "nw-audit-key-" });
  try {
    await Deno.writeTextFile(`${dir}/.zai`, "bare-key-value\n");
    assertEquals(await loadApiKey(() => undefined, dir), "bare-key-value");
    await Deno.writeTextFile(`${dir}/.zai`, 'ZAI_API_KEY="assigned-value"\n');
    assertEquals(await loadApiKey(() => undefined, dir), "assigned-value");
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("loadApiKey returns null rather than throwing when there is no key", async () => {
  assertEquals(await loadApiKey(() => undefined, "/nonexistent/home"), null);
});

// --- JSON extraction ----------------------------------------------------------------------------

Deno.test("a JSON array is found inside a fenced block and inside prose", () => {
  assertEquals(extractJsonArray('```json\n[{"a":1}]\n```'), [{ a: 1 }]);
  assertEquals(extractJsonArray('Here is what I found:\n[{"a":1}]\nHope that helps.'), [{ a: 1 }]);
  assertEquals(extractJsonArray("[]"), []);
});

Deno.test("a bracket inside a string does not end the array early", () => {
  assertEquals(extractJsonArray('[{"what":"a ] bracket"}]'), [{ what: "a ] bracket" }]);
});

Deno.test("prose with no array at all yields null, not an invented result", () => {
  assertEquals(extractJsonArray("I could not find any problems in this directory."), null);
});

// --- prompt construction --------------------------------------------------------------------------

Deno.test("the reader prompt numbers every line so a citation can be checked", () => {
  const prompt = buildReaderPrompt("libs/a", [FILE]);
  assertStringIncludes(prompt, "--- libs/a/Tokens.cs ---");
  assertStringIncludes(prompt, "3\t    return null;");
});

Deno.test("the refuter prompt carries the claim and the code around it", () => {
  const candidate = toCandidates(
    [{ line: 3, file: FILE.path, what: "w", why: "y", confirm: "c" }],
    [FILE],
  ).candidates[0];
  const prompt = buildRefuterPrompt(candidate, FILE);
  assertStringIncludes(prompt, "libs/a/Tokens.cs:3");
  assertStringIncludes(prompt, "return null;");
});

// --- turning model output into candidates -----------------------------------------------------------

Deno.test("a well-formed finding becomes a candidate whose excerpt comes from the real file", () => {
  const { candidates, discarded } = toCandidates(
    [{ line: 3, file: "libs/a/Tokens.cs", what: "Returns null unconditionally.", why: "Callers deref it.", confirm: "read line 3" }],
    [FILE],
  );
  assertEquals(discarded, 0);
  assertEquals(candidates.length, 1);
  assertEquals(candidates[0].ruleId, "reading-pass");
  assertEquals(candidates[0].verified, "unverified");
  assertStringIncludes(candidates[0].excerpt, "return null;");
  assertEquals(validate(candidates[0]), null);
});

Deno.test("a citation past the end of the file is discarded", () => {
  const out = toCandidates([{ line: 900, file: FILE.path, what: "w", why: "y", confirm: "c" }], [FILE]);
  assertEquals(out.candidates.length, 0);
  assertEquals(out.discarded, 1);
});

Deno.test("a finding missing any reader-facing field is discarded", () => {
  for (const missing of ["what", "why", "confirm"]) {
    const item: Record<string, unknown> = { line: 1, file: FILE.path, what: "w", why: "y", confirm: "c" };
    delete item[missing];
    assertEquals(toCandidates([item], [FILE]).discarded, 1, `should discard without ${missing}`);
  }
});

Deno.test("a finding naming a file outside the slice is discarded", () => {
  const out = toCandidates([{ line: 1, file: "somewhere/else.cs", what: "w", why: "y", confirm: "c" }], [FILE]);
  assertEquals(out.discarded, 1);
});

Deno.test("non-objects in the array are discarded rather than crashing the run", () => {
  assertEquals(toCandidates(["oops", null, 3], [FILE]).discarded, 3);
});

// --- verdicts -----------------------------------------------------------------------------------

Deno.test("a verdict is parsed out of a chatty answer", () => {
  assertEquals(parseVerdict('Sure: {"refuted": true, "reason": "guard is above"}'), {
    refuted: true,
    reason: "guard is above",
  });
});

Deno.test("an unparseable verdict is null, not a default of 'fine'", () => {
  assertEquals(parseVerdict("I think it is probably fine."), null);
  assertEquals(parseVerdict('{"reason": "no boolean here"}'), null);
});

// --- the pass end to end (R3, R10, R11) ------------------------------------------------------------

const ONE_FINDING = JSON.stringify([
  { line: 3, file: "libs/a/Tokens.cs", what: "Returns null unconditionally.", why: "Callers deref it.", confirm: "read line 3" },
]);

Deno.test("a refuted finding never reaches the ledger, and the refutation is counted", async () => {
  const { transport } = stub([ONE_FINDING, '{"refuted": true, "reason": "nullable is handled by the caller"}']);
  const result = await readingPass("libs/a", [FILE], { ...OPTS, transport });
  assertEquals(result.candidates, []);
  assertEquals(result.refuted, 1);
  assertEquals(result.unevaluated, 0);
});

Deno.test("a finding that survives refutation is admitted as verified", async () => {
  const { transport } = stub([ONE_FINDING, '{"refuted": false, "reason": "could not break it"}']);
  const result = await readingPass("libs/a", [FILE], { ...OPTS, transport });
  assertEquals(result.candidates.length, 1);
  assertEquals(result.candidates[0].verified, "verified");
  assertEquals(result.refuted, 0);
});

Deno.test("a finding the refuter could not evaluate is admitted as unverified", async () => {
  const { transport } = stub([ONE_FINDING, "I am not sure what to make of this."]);
  const result = await readingPass("libs/a", [FILE], { ...OPTS, transport });
  assertEquals(result.candidates.length, 1);
  assertEquals(result.candidates[0].verified, "unverified");
  assertEquals(result.unevaluated, 1);
  assertEquals(result.refuted, 0);
});

Deno.test("token usage sums the reading call and every refutation call", async () => {
  const { transport } = stub([ONE_FINDING, '{"refuted": false, "reason": "r"}']);
  const result = await readingPass("libs/a", [FILE], { ...OPTS, transport });
  assertEquals(result.usage, { inputTokens: 20, outputTokens: 10 });
});

Deno.test("a slice over the byte bound is not read at all, and says so", async () => {
  const { transport, seen } = stub([ONE_FINDING]);
  const result = await readingPass("libs/a", [FILE], { ...OPTS, transport, sliceByteBound: 10 });
  assertEquals(result.bounded, true);
  assertEquals(result.candidates, []);
  assertEquals(seen.length, 0, "no model call may be made for a slice over the bound");
  assertEquals(sliceBytes([FILE]) > 10, true);
});

Deno.test("a model answering in prose produces no findings rather than invented ones", async () => {
  const { transport } = stub(["This directory looks fine to me."]);
  const result = await readingPass("libs/a", [FILE], { ...OPTS, transport });
  assertEquals(result.candidates, []);
  assertEquals(result.refuted, 0);
});

Deno.test("an empty slice makes no model call", async () => {
  const { transport, seen } = stub([ONE_FINDING]);
  assertEquals((await readingPass("libs/a", [], { ...OPTS, transport })).candidates, []);
  assertEquals(seen.length, 0);
});

Deno.test("a refuter that throws leaves the finding unverified rather than dropping it", async () => {
  let call = 0;
  const transport = (_req: Request) => {
    call++;
    if (call === 1) {
      return Promise.resolve(
        new Response(JSON.stringify({ content: [{ type: "text", text: ONE_FINDING }], usage: {} }), { status: 200 }),
      );
    }
    return Promise.reject(new Error("network down"));
  };
  const result = await readingPass("libs/a", [FILE], { ...OPTS, transport });
  assertEquals(result.candidates.length, 1);
  assertEquals(result.candidates[0].verified, "unverified");
  assertEquals(result.unevaluated, 1);
});
