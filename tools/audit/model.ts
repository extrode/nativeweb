// A small Anthropic-Messages client for the z.ai endpoint, and nothing else.
//
// Why not the Claude CLI: the worker tooling that would normally route this lives under
// ~/.claude on the workstation and is not this repository's to install on an audit host, and
// `claude login` on a headless Mac mini is a browser flow that has to be redone by hand every
// time the credential rotates. One fetch against a documented endpoint has neither problem.
//
// Why not glm-4.7-flash, which is the free tier: the delegate notes record it at 1/4 on the
// reasoning benchmark with the instruction "do not give it anything to work out". A reading
// pass is entirely working something out. So this draws z.ai plan credits, the run records what
// it spent (R11), and the digest shows the trend so drift is visible before it is a bill.

export const ZAI_ANTHROPIC_URL = "https://api.z.ai/api/anthropic";
export const DEFAULT_MODEL = "glm-5.3";

export interface Usage {
  inputTokens: number;
  outputTokens: number;
}

export interface Completion {
  text: string;
  usage: Usage;
  /** True when the model stopped because it hit `maxTokens` rather than finishing. */
  truncated: boolean;
}

/** The seam every test uses instead of the network. */
export type Transport = (request: Request) => Promise<Response>;

export interface ModelOptions {
  apiKey: string;
  model?: string;
  baseUrl?: string;
  maxTokens?: number;
  temperature?: number;
  transport?: Transport;
  /** Attempts on a retryable failure, including the first. */
  attempts?: number;
  /** Injected so tests do not sleep. */
  sleep?: (ms: number) => Promise<void>;
}

export class ModelError extends Error {
  constructor(message: string, readonly status?: number, readonly retryable = false) {
    super(message);
    this.name = "ModelError";
  }
}

/**
 * Resolve the key from the environment, falling back to `~/.zai`.
 *
 * The file holds either a bare key or `ZAI_API_KEY=<key>`; both are accepted, because the one
 * on this machine is a bare key and the exported form is what a reader would expect.
 */
export async function loadApiKey(
  env: (k: string) => string | undefined = Deno.env.get,
  home = env("HOME") ?? env("USERPROFILE") ?? "",
): Promise<string | null> {
  const fromEnv = env("ZAI_API_KEY");
  if (fromEnv !== undefined && fromEnv.trim().length > 0) return fromEnv.trim();
  if (home.length === 0) return null;
  try {
    const raw = (await Deno.readTextFile(`${home}/.zai`)).trim();
    if (raw.length === 0) return null;
    const assigned = /^[A-Za-z_][A-Za-z0-9_]*\s*=\s*(.+)$/.exec(raw);
    return (assigned?.[1] ?? raw).trim().replace(/^["']|["']$/g, "");
  } catch {
    return null;
  }
}

const RETRYABLE = new Set([408, 429, 500, 502, 503, 504]);

export function isRetryable(status: number): boolean {
  return RETRYABLE.has(status);
}

/** One completion. Returns the assistant's text and what it cost in tokens. */
export async function complete(
  system: string,
  user: string,
  options: ModelOptions,
): Promise<Completion> {
  const {
    apiKey,
    model = DEFAULT_MODEL,
    baseUrl = ZAI_ANTHROPIC_URL,
    maxTokens = 4096,
    temperature = 0,
    transport = fetch,
    attempts = 3,
    sleep = (ms: number) => new Promise((r) => setTimeout(r, ms)),
  } = options;

  const body = JSON.stringify({
    model,
    max_tokens: maxTokens,
    temperature,
    system,
    messages: [{ role: "user", content: user }],
  });

  let lastError: ModelError | null = null;
  for (let attempt = 1; attempt <= Math.max(1, attempts); attempt++) {
    const request = new Request(`${baseUrl}/v1/messages`, {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-api-key": apiKey,
        "anthropic-version": "2023-06-01",
      },
      body,
    });

    let response: Response;
    try {
      response = await transport(request);
    } catch (cause) {
      lastError = new ModelError(`transport failed: ${cause}`, undefined, true);
      if (attempt < attempts) await sleep(250 * attempt);
      continue;
    }

    if (!response.ok) {
      const detail = (await response.text()).slice(0, 400);
      lastError = new ModelError(
        `${response.status} from ${baseUrl}: ${detail}`,
        response.status,
        isRetryable(response.status),
      );
      if (lastError.retryable && attempt < attempts) {
        await sleep(250 * attempt);
        continue;
      }
      throw lastError;
    }

    const payload = await response.json() as {
      content?: Array<{ type?: string; text?: string }>;
      usage?: { input_tokens?: number; output_tokens?: number };
      stop_reason?: string;
    };
    const text = (payload.content ?? [])
      .filter((block) => block.type === undefined || block.type === "text")
      .map((block) => block.text ?? "")
      .join("");
    return {
      text,
      usage: {
        inputTokens: payload.usage?.input_tokens ?? 0,
        outputTokens: payload.usage?.output_tokens ?? 0,
      },
      truncated: payload.stop_reason === "max_tokens",
    };
  }
  throw lastError ?? new ModelError("no attempt was made", undefined, false);
}

/**
 * Pull the first JSON array out of a model response.
 *
 * A model asked for JSON returns JSON surrounded by an apology, a fenced block, or a sentence
 * explaining what it did. Parsing the whole response is the brittle choice; finding the array is
 * the one that survives a chatty model.
 */
export function extractJsonArray(text: string): unknown[] | null {
  const fenced = /```(?:json)?\s*([\s\S]*?)```/.exec(text);
  const candidates = [fenced?.[1], text].filter((c): c is string => typeof c === "string");
  for (const candidate of candidates) {
    const start = candidate.indexOf("[");
    if (start === -1) continue;
    // Walk to the matching bracket rather than the last one, so trailing prose is ignored.
    let depth = 0;
    let inString = false;
    let escaped = false;
    for (let i = start; i < candidate.length; i++) {
      const c = candidate[i];
      if (escaped) {
        escaped = false;
        continue;
      }
      if (c === "\\") {
        escaped = true;
        continue;
      }
      if (c === '"') inString = !inString;
      if (inString) continue;
      if (c === "[") depth++;
      else if (c === "]") {
        depth--;
        if (depth === 0) {
          try {
            const parsed = JSON.parse(candidate.slice(start, i + 1));
            if (Array.isArray(parsed)) return parsed;
          } catch {
            // Fall through to the next candidate.
          }
          break;
        }
      }
    }
  }
  return null;
}
