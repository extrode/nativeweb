// A test that runs code and asserts nothing.
//
// Straight out of the project's own testing rule: "A test that passes because the code under it
// never executed asserts nothing." The sibling failure is a test that executes plenty and then
// checks none of it — it goes green forever, it counts in the suite total, and it is indis-
// tinguishable from coverage until the day it should have failed and did not.
//
// Covers the two frameworks in this repository: xUnit facts and theories in C#, and Deno.test
// in the audit tooling itself. A test whose whole body is an `await`ed call that would throw on
// failure is not a defect — `assertThrows`, `Assert.Throws`, and a bare call to something that
// validates internally all count as assertions, and are listed below.

import type { Candidate } from "../ledger.ts";
import { type Check, type CheckContext, excerptAt, lineAt } from "./types.ts";

/** Anything on this list means the test checks something. Deliberately generous. */
const ASSERTION = [
  /\bAssert\s*\./, //                   xUnit
  // A project-local assertion helper: AssertRfcError(call, ...), AssertSingleDiagnostic(...).
  // Without this the rule reported 52 of this repository's own tests, every one of them a test
  // that delegates its assertion to a helper one scroll up in the same file -- which is the
  // normal way to write a suite of near-identical protocol facts, not a defect. A rule whose
  // output is 42% noise is a rule whose output stops being read, and the daily run was spending
  // its refutation budget on it. Prefixing a method `Assert` and having it assert nothing is a
  // lie the rule is entitled to believe.
  /\bAssert\w*\s*\(/,
  /\bassert[A-Z_]?\w*\s*\(/, //         @std/assert: assertEquals, assert(, assertThrows
  /\bassert\s*\(/,
  /\.Should\s*\(\s*\)/, //              FluentAssertions
  /\bShould\w+\s*\(/,
  /\bVerify\s*(?:All)?\s*\(/, //        Moq
  /\bExpect\w*\s*\(/, //                Playwright: Expect(page)...
  /\bawait\s+expect\s*\(/,
  /\bMustHaveHappened\s*\(/,
  /\bthrow\s+new\s+\w*(?:Assertion|Xunit)/,
];

const CSHARP_ATTRIBUTE = /^\s*\[\s*(Fact|Theory)\b/;
const CSHARP_SIGNATURE = /\b(?:public|internal|private)\s+(?:async\s+)?[\w<>,\[\]\s.?]+\s+(\w+)\s*\(/;

/**
 * How far past a `[Fact]` to look for the method it decorates. Enough for a stack of attributes
 * and a signature broken over several lines; not enough to reach the next test.
 */
const SIGNATURE_WINDOW_LINES = 12;
const DENO_TEST = /Deno\.test\s*\(/;

function hasAssertion(body: string): boolean {
  return ASSERTION.some((re) => re.test(body));
}

/** From the opening brace at or after `from`, the body up to its matching close. */
export function braceBody(text: string, from: number): { body: string; end: number } | null {
  const open = text.indexOf("{", from);
  if (open === -1) return null;
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    const c = text[i];
    if (c === "{") depth++;
    else if (c === "}") {
      depth--;
      if (depth === 0) return { body: text.slice(open + 1, i), end: i };
    }
  }
  return null;
}

/**
 * Blank out comments and string literals, so `// Assert.Equal(...)` does not count as an
 * assertion and `text: "// TODO"` does not eat the rest of its line.
 *
 * One scanner rather than a sequence of regex replaces. The sequential form is wrong in a way
 * that is invisible until it bites: stripping `//` comments before string literals means a `//`
 * *inside* a string starts a comment, the rest of the line disappears, and every brace on it
 * goes with it — which desynchronises the block matcher for the remainder of the file. This
 * rule's own dogfood test found exactly that.
 *
 * Length and line breaks are preserved, so offsets computed on the stripped text still map to
 * the original's line numbers.
 */
export function stripInert(source: string): string {
  const out = source.split("");
  const blank = (from: number, to: number) => {
    for (let i = from; i < to && i < out.length; i++) {
      if (out[i] !== "\n" && out[i] !== "\r") out[i] = " ";
    }
  };
  let i = 0;
  while (i < source.length) {
    const c = source[i];
    const next = source[i + 1];

    if (c === "/" && next === "/") {
      const end = source.indexOf("\n", i);
      blank(i, end === -1 ? source.length : end);
      i = end === -1 ? source.length : end;
      continue;
    }
    if (c === "/" && next === "*") {
      const end = source.indexOf("*/", i + 2);
      const stop = end === -1 ? source.length : end + 2;
      blank(i, stop);
      i = stop;
      continue;
    }
    // C# raw string literal: """ ... """
    if (c === '"' && next === '"' && source[i + 2] === '"') {
      const end = source.indexOf('"""', i + 3);
      const stop = end === -1 ? source.length : end + 3;
      blank(i, stop);
      i = stop;
      continue;
    }
    // C# verbatim string: @" ... ", where "" is an escaped quote and \ is not an escape.
    if (c === "@" && next === '"') {
      let j = i + 2;
      while (j < source.length) {
        if (source[j] === '"') {
          if (source[j + 1] === '"') j += 2;
          else break;
        } else j++;
      }
      blank(i, j + 1);
      i = j + 1;
      continue;
    }
    if (c === '"' || c === "'" || c === "`") {
      let j = i + 1;
      while (j < source.length) {
        if (source[j] === "\\") {
          j += 2;
          continue;
        }
        if (source[j] === c) break;
        // A plain quote does not span lines; a template literal does.
        if (c !== "`" && source[j] === "\n") break;
        j++;
      }
      blank(i, j + 1);
      i = j + 1;
      continue;
    }
    i++;
  }
  return out.join("");
}

export const assertionlessTest: Check = {
  ruleId: "assertionless-test",
  describe: "A test method or Deno.test case whose body contains no assertion.",
  run(ctx: CheckContext): Promise<Candidate[]> {
    const found: Candidate[] = [];

    for (const file of ctx.files) {
      const lower = file.path.toLowerCase();
      const isCSharpTest = lower.endsWith(".cs") &&
        (/tests?\//.test(lower) || /tests?\.cs$/.test(lower));
      const isTsTest = lower.endsWith(".test.ts");
      if (!isCSharpTest && !isTsTest) continue;

      const stripped = stripInert(file.text);

      if (isCSharpTest) {
        const lines = stripped.split(/\r?\n/);
        let offset = 0;
        for (let i = 0; i < lines.length; i++) {
          const lineStart = offset;
          offset += lines[i].length + 1;
          if (!CSHARP_ATTRIBUTE.test(lines[i])) continue;
          // Find the method signature after the attribute block, then its body. Bounded to the
          // next few lines: an unbounded search over the rest of the file binds to some later,
          // unrelated method whenever the attributed one's signature does not match the regex
          // (a generic method, an unusual return type), and then names that method at this
          // attribute's line. A confidently wrong finding is worse than a missed one.
          const window = lines.slice(i, i + SIGNATURE_WINDOW_LINES).join("\n");
          const rest = stripped.slice(lineStart);
          const sig = CSHARP_SIGNATURE.exec(window);
          if (sig === null) continue;
          const block = braceBody(rest, sig.index);
          // An expression-bodied or abstract test has no block to inspect; leave it alone.
          if (block === null || block.body.trim().length === 0) continue;
          if (hasAssertion(block.body)) continue;

          const line = i + 1;
          found.push({
            ruleId: "assertionless-test",
            path: file.path,
            line,
            what: `Test \`${sig[1]}\` runs code and asserts nothing.`,
            why:
              "It counts toward the suite total and goes green whatever the code does, so it " +
              "reads as coverage while providing none. It cannot fail when the behaviour it " +
              "names breaks.",
            confirm:
              `Open ${file.path} at line ${line} and read the body of ${sig[1]}: there is no ` +
              "Assert, Should, Verify or Expect call in it.",
            excerpt: excerptAt(file.text, line, 4),
            verified: "verified",
          });
        }
      }

      if (isTsTest) {
        const re = new RegExp(DENO_TEST.source, "g");
        let m: RegExpExecArray | null;
        while ((m = re.exec(stripped)) !== null) {
          const block = braceBody(stripped, m.index);
          if (block === null || block.body.trim().length === 0) continue;
          if (hasAssertion(block.body)) continue;
          const line = lineAt(stripped, m.index);
          found.push({
            ruleId: "assertionless-test",
            path: file.path,
            line,
            what: "A Deno.test case runs code and asserts nothing.",
            why:
              "It goes green whatever the code does, so it reads as coverage while providing " +
              "none. It cannot fail when the behaviour it names breaks.",
            confirm: `Open ${file.path} at line ${line}: the case body contains no assert call.`,
            excerpt: excerptAt(file.text, line, 4),
            verified: "verified",
          });
        }
      }
    }
    return Promise.resolve(found);
  },
};
