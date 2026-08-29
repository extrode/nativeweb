// R13: no credential, key, token, connection string or customer data reaches a report.
//
// Two jobs, and they are not the same job:
//
//   redactCandidate  runs on the write path, before a finding is hashed or stored. Redaction
//                    must precede identity, or the same finding hashes differently depending on
//                    whether a secret happened to be in the excerpt.
//   scanCorpus       runs over everything already written. A pattern added in November has to
//                    catch what was written in September, so this is not a one-time check at
//                    write time — it is an assertion over the whole corpus.
//
// Where a finding is *about* a secret, the point is the location, not the value. Every rule here
// keeps the surrounding text and replaces only the secret, so "hard-coded password at Db.cs:41"
// survives redaction intact and still tells the reader where to look.

import type { Candidate } from "./ledger.ts";

export interface Rule {
  kind: string;
  pattern: RegExp;
  /** Which capture group holds the secret itself. 0 means the whole match. */
  group: number;
}

/**
 * Ordered: the first rule to match a span wins, so the specific named-vendor rules sit above
 * the generic assignment rules that would otherwise swallow them under a vaguer label.
 */
export const RULES: readonly Rule[] = [
  { kind: "pem-private-key", group: 0, pattern: /-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----/g },
  { kind: "aws-access-key-id", group: 0, pattern: /\b(?:AKIA|ASIA|AGPA|AIDA|AROA|ANPA|ANVA|ABIA|ACCA)[0-9A-Z]{16}\b/g },
  { kind: "github-token", group: 0, pattern: /\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}\b/g },
  { kind: "github-pat", group: 0, pattern: /\bgithub_pat_[A-Za-z0-9_]{22,}\b/g },
  { kind: "slack-token", group: 0, pattern: /\bxox[abposr]-[A-Za-z0-9-]{10,}\b/g },
  { kind: "google-api-key", group: 0, pattern: /\bAIza[0-9A-Za-z_-]{35}\b/g },
  { kind: "openai-style-key", group: 0, pattern: /\bsk-(?:proj-|ant-|or-)?[A-Za-z0-9_-]{20,}\b/g },
  { kind: "zai-key", group: 0, pattern: /\b[0-9a-f]{32}\.[A-Za-z0-9]{16}\b/g },
  { kind: "jwt", group: 0, pattern: /\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/g },
  { kind: "bearer-token", group: 1, pattern: /\b[Bb]earer\s+([A-Za-z0-9._~+/-]{20,}=*)/g },
  { kind: "basic-auth-in-url", group: 1, pattern: /\b[a-z][a-z0-9+.-]*:\/\/[^\s:/@]+:([^\s@]+)@/g },
  { kind: "connection-string-password", group: 1, pattern: /\b(?:Password|Pwd|User Id|UserId)\s*=\s*([^;\s"']+)/gi },
  { kind: "assigned-secret", group: 2, pattern: /\b([A-Za-z_][A-Za-z0-9_]*(?:SECRET|PASSWORD|PASSWD|APIKEY|API_KEY|TOKEN|PRIVATE_KEY|CLIENT_SECRET|ACCESS_KEY)[A-Za-z0-9_]*)\s*[:=]\s*["']?([^\s"',;)]{8,})["']?/gi },
];

export interface Match {
  kind: string;
  /** Zero-based offset in the scanned text. Never the value itself. */
  index: number;
  /** Character length of the redacted span, so a diff can be located without quoting it. */
  length: number;
}

/** Names of things that look like secrets but are placeholders, so a doc example stays readable. */
const PLACEHOLDERS = /^(?:x{3,}|\.{3,}|<[^>]*>|\[[^\]]*\]|\$\{[^}]*\}|%[A-Za-z_]+%|changeme|placeholder|example|redacted|your[-_]?\w*|dummy|fake|test|sample|null|none|todo)$/i;

function isPlaceholder(value: string): boolean {
  return PLACEHOLDERS.test(value.trim()) || /^\[redacted:/.test(value.trim());
}

/** Every secret-looking span in `text`, as locations. Deliberately returns no values. */
export function scanText(text: string): Match[] {
  const matches: Match[] = [];
  const claimed: Array<[number, number]> = [];
  for (const rule of RULES) {
    const re = new RegExp(rule.pattern.source, rule.pattern.flags);
    let m: RegExpExecArray | null;
    while ((m = re.exec(text)) !== null) {
      if (m[0].length === 0) {
        re.lastIndex++;
        continue;
      }
      const value = rule.group === 0 ? m[0] : m[rule.group];
      if (value === undefined || isPlaceholder(value)) continue;
      const offset = rule.group === 0 ? m.index : m.index + m[0].indexOf(value);
      // First rule to claim a span wins, so a JWT is not also reported as an assigned secret.
      if (claimed.some(([s, e]) => offset < e && offset + value.length > s)) continue;
      claimed.push([offset, offset + value.length]);
      matches.push({ kind: rule.kind, index: offset, length: value.length });
    }
  }
  return matches.sort((a, b) => a.index - b.index);
}

/** Replace every secret-looking span with a labelled marker, keeping everything around it. */
export function redactText(text: string): string {
  const matches = scanText(text);
  if (matches.length === 0) return text;
  let out = "";
  let cursor = 0;
  for (const match of matches) {
    out += text.slice(cursor, match.index) + `[redacted:${match.kind}]`;
    cursor = match.index + match.length;
  }
  return out + text.slice(cursor);
}

export interface RedactionReport {
  candidate: Candidate;
  /** Which fields carried something, and what kind. Never the value. */
  redacted: Array<{ field: string; kind: string }>;
}

const FIELDS = ["what", "why", "confirm", "excerpt"] as const;

/**
 * Redact a candidate on the write path. Must be called before `computeId`, so that identity is
 * computed over the redacted excerpt and does not shift when a rule is added later.
 */
export function redactCandidate(candidate: Candidate): RedactionReport {
  const redacted: Array<{ field: string; kind: string }> = [];
  const next = { ...candidate };
  for (const field of FIELDS) {
    const value = candidate[field];
    const matches = scanText(value);
    if (matches.length === 0) continue;
    for (const m of matches) redacted.push({ field, kind: m.kind });
    next[field] = redactText(value);
  }
  return { candidate: next, redacted };
}

export interface CorpusFinding {
  file: string;
  line: number;
  kind: string;
}

/** The R13 assertion: every secret-looking span anywhere in the written corpus, as locations. */
export function scanCorpus(files: ReadonlyArray<{ path: string; text: string }>): CorpusFinding[] {
  const found: CorpusFinding[] = [];
  for (const file of files) {
    const lines = file.text.split(/\r?\n/);
    lines.forEach((line, i) => {
      for (const m of scanText(line)) found.push({ file: file.path, line: i + 1, kind: m.kind });
    });
  }
  return found;
}
