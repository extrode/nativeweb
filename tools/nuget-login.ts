/**
 * Trusted publishing to nuget.org: trades the job's GitHub OIDC token for a
 * nuget.org API key valid for one hour, and prints the key on stdout.
 *
 *   key="$(deno run -A tools/nuget-login.ts)"
 *
 * The same two requests NuGet/login@v1 makes (its src/index.ts at 8d19675),
 * done here because that action is a node24 `uses:` step and decision
 * 2026-08-20-001 keeps this repository to one of those (actions/checkout).
 * jaunty and jauntyq publish through the action itself.
 *
 * Needs `id-token: write` on the job, which is what puts
 * ACTIONS_ID_TOKEN_REQUEST_URL and ACTIONS_ID_TOKEN_REQUEST_TOKEN in the
 * environment, and NUGET_USER: the nuget.org profile name that created the
 * trusted-publishing policy, not an email address.
 */

export const AUDIENCE = "https://www.nuget.org";
export const TOKEN_SERVICE = "https://www.nuget.org/api/v2/token";

export interface LoginEnv {
  requestUrl?: string;
  requestToken?: string;
  user?: string;
}

export async function exchange(env: LoginEnv, fetchFn: typeof fetch = fetch): Promise<string> {
  if (!env.requestUrl || !env.requestToken) {
    throw new Error(
      "GitHub OIDC is not available: ACTIONS_ID_TOKEN_REQUEST_URL or ACTIONS_ID_TOKEN_REQUEST_TOKEN "
        + "is unset. The job needs `permissions: id-token: write`.",
    );
  }
  if (!env.user) {
    throw new Error("NUGET_USER is unset. Set the repository variable to the nuget.org profile name.");
  }

  const oidcResponse = await fetchFn(`${env.requestUrl}&audience=${encodeURIComponent(AUDIENCE)}`, {
    headers: { Authorization: `Bearer ${env.requestToken}` },
  });
  const oidc = oidcResponse.ok ? (await oidcResponse.json()).value : undefined;
  if (typeof oidc !== "string" || oidc.length === 0) {
    throw new Error(`GitHub issued no OIDC token (HTTP ${oidcResponse.status}).`);
  }

  const keyResponse = await fetchFn(TOKEN_SERVICE, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${oidc}`,
      "User-Agent": "nuget/login-action",
    },
    body: JSON.stringify({ username: env.user, tokenType: "ApiKey" }),
  });
  const body = await keyResponse.text();
  if (keyResponse.status !== 200) {
    throw new Error(
      `Token exchange failed (HTTP ${keyResponse.status}) at ${TOKEN_SERVICE}. NUGET_USER must be the `
        + `policy creator, and the policy must name publish.yml with no environment: ${body}`,
    );
  }

  const apiKey = JSON.parse(body).apiKey;
  if (typeof apiKey !== "string" || apiKey.length === 0) {
    throw new Error('The token service answered 200 with no "apiKey".');
  }
  return apiKey;
}

if (import.meta.main) {
  try {
    const key = await exchange({
      requestUrl: Deno.env.get("ACTIONS_ID_TOKEN_REQUEST_URL"),
      requestToken: Deno.env.get("ACTIONS_ID_TOKEN_REQUEST_TOKEN"),
      user: Deno.env.get("NUGET_USER"),
    });
    console.log(key);
  } catch (error) {
    console.error((error as Error).message);
    Deno.exit(1);
  }
}
