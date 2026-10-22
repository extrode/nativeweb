import { assertEquals, assertRejects, assertStringIncludes } from "jsr:@std/assert@1";

import { AUDIENCE, exchange, TOKEN_SERVICE } from "./nuget-login.ts";

const env = { requestUrl: "https://oidc.example/token?x=1", requestToken: "request-token", user: "extrode" };

interface Call {
  url: string;
  init?: RequestInit;
}

function fakeFetch(oidc: Response, key: Response): { fetchFn: typeof fetch; calls: Call[] } {
  const calls: Call[] = [];
  const responses = [oidc, key];
  const fetchFn = ((input: string | URL | Request, init?: RequestInit) => {
    calls.push({ url: String(input), init });
    return Promise.resolve(responses[calls.length - 1]);
  }) as typeof fetch;
  return { fetchFn, calls };
}

Deno.test("exchange trades the OIDC token for the key nuget.org issues", async () => {
  const { fetchFn, calls } = fakeFetch(
    Response.json({ value: "oidc-token" }),
    Response.json({ apiKey: "short-lived-key" }),
  );

  assertEquals(await exchange(env, fetchFn), "short-lived-key");

  assertEquals(calls[0].url, `${env.requestUrl}&audience=${encodeURIComponent(AUDIENCE)}`);
  assertEquals(new Headers(calls[0].init?.headers).get("Authorization"), "Bearer request-token");

  assertEquals(calls[1].url, TOKEN_SERVICE);
  assertEquals(calls[1].init?.method, "POST");
  assertEquals(new Headers(calls[1].init?.headers).get("Authorization"), "Bearer oidc-token");
  assertEquals(JSON.parse(String(calls[1].init?.body)), { username: "extrode", tokenType: "ApiKey" });
});

Deno.test("exchange refuses a job without id-token: write", async () => {
  const { fetchFn, calls } = fakeFetch(Response.json({}), Response.json({}));

  const error = await assertRejects(() => exchange({ user: "extrode" }, fetchFn));

  assertStringIncludes((error as Error).message, "id-token: write");
  assertEquals(calls.length, 0);
});

Deno.test("exchange refuses an unset NUGET_USER before asking for a token", async () => {
  const { fetchFn, calls } = fakeFetch(Response.json({}), Response.json({}));

  const error = await assertRejects(() => exchange({ ...env, user: "" }, fetchFn));

  assertStringIncludes((error as Error).message, "NUGET_USER");
  assertEquals(calls.length, 0);
});

Deno.test("exchange refuses when GitHub issues no OIDC token", async () => {
  const { fetchFn, calls } = fakeFetch(new Response("denied", { status: 403 }), Response.json({}));

  const error = await assertRejects(() => exchange(env, fetchFn));

  assertStringIncludes((error as Error).message, "HTTP 403");
  assertEquals(calls.length, 1);
});

Deno.test("exchange reports nuget.org's reason when the policy does not match", async () => {
  const { fetchFn } = fakeFetch(
    Response.json({ value: "oidc-token" }),
    Response.json({ error: "no matching policy" }, { status: 401 }),
  );

  const error = await assertRejects(() => exchange(env, fetchFn));

  assertStringIncludes((error as Error).message, "HTTP 401");
  assertStringIncludes((error as Error).message, "no matching policy");
});

Deno.test("exchange refuses a 200 that carries no key", async () => {
  const { fetchFn } = fakeFetch(Response.json({ value: "oidc-token" }), Response.json({}));

  const error = await assertRejects(() => exchange(env, fetchFn));

  assertStringIncludes((error as Error).message, "apiKey");
});
