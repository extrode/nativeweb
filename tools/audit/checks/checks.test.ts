import { assert, assertEquals } from "@std/assert";
import { type Blame, type CheckContext, excerptAt, lineAt, noBlame, noReadFile, type SourceFile } from "./types.ts";
import { isAttributed, makeStaleTodo, staleTodo } from "./stale-todo.ts";
import { cleanPath, docNamesMissingFile, looksLikeRepoPath, resolutions, resolvesAnywhere } from "./doc-names-missing-file.ts";
import { assertionlessTest, braceBody, stripInert } from "./assertionless-test.ts";
import { validate } from "../ledger.ts";

const NOW = new Date("2026-08-30T00:00:00.000Z");

function ctx(files: SourceFile[], over: Partial<CheckContext> = {}): CheckContext {
  return {
    slice: "x",
    files,
    allPaths: new Set<string>(),
    now: NOW,
    blame: noBlame,
    readFile: noReadFile,
    ...over,
  };
}

function blameAt(iso: string, name = "sy"): CheckContext["blame"] {
  const b: Blame = { authorTime: new Date(iso), authorName: name };
  return () => Promise.resolve(b);
}

// --- shared helpers --------------------------------------------------------------------------

Deno.test("lineAt and excerptAt agree on a 1-based line number", () => {
  const text = "one\ntwo\nthree\nfour";
  assertEquals(lineAt(text, 0), 1);
  assertEquals(lineAt(text, text.indexOf("three")), 3);
  assertEquals(excerptAt(text, 3, 2), "three\nfour");
});

// --- stale-todo ------------------------------------------------------------------------------

const OLD = "2025-06-01T00:00:00.000Z"; // ~455 days before NOW
const RECENT = "2026-08-01T00:00:00.000Z"; // 29 days before NOW

Deno.test("an old, unattributed TODO is reported with its age and a blame command", async () => {
  const file = { path: "libs/a/Foo.cs", text: "class A {\n  // TODO: revisit\n  int x;\n}\n" };
  const found = await staleTodo.run(ctx([file], { blame: blameAt(OLD) }));
  assertEquals(found.length, 1);
  assertEquals(found[0].line, 2);
  assertEquals(found[0].ruleId, "stale-todo");
  assert(found[0].what.includes("455"), `expected an age in: ${found[0].what}`);
  assert(found[0].confirm.startsWith("git blame -L 2,2"));
  assertEquals(validate(found[0]), null, "a produced finding must satisfy R6");
});

Deno.test("a recent TODO is not reported however unattributed", async () => {
  const file = { path: "a/Foo.cs", text: "// TODO: revisit\n" };
  assertEquals((await staleTodo.run(ctx([file], { blame: blameAt(RECENT) }))).length, 0);
});

Deno.test("an attributed TODO is not reported however old", async () => {
  const attributed = [
    "// TODO(sy): revisit",
    "// TODO: #412 revisit",
    "// TODO: see https://github.com/dotnet/aspnetcore/issues/1",
    "// TODO: spec 011 covers this",
    "// TODO: R14",
    "// TODO by 2026-12-01",
  ];
  for (const text of attributed) {
    const found = await staleTodo.run(ctx([{ path: "a/F.cs", text }], { blame: blameAt(OLD) }));
    assertEquals(found.length, 0, `should be attributed: ${text}`);
  }
});

Deno.test("isAttributed is the half of the rule that does not need git", () => {
  assertEquals(isAttributed("(sy): thing"), true);
  assertEquals(isAttributed(": #99"), true);
  assertEquals(isAttributed(": revisit this later"), false);
});

Deno.test("all four marker kinds are recognised", async () => {
  for (const kind of ["TODO", "FIXME", "HACK", "XXX"]) {
    const found = await staleTodo.run(
      ctx([{ path: "a/F.cs", text: `// ${kind}: fix` }], { blame: blameAt(OLD) }),
    );
    assertEquals(found.length, 1, `${kind} should be recognised`);
    assert(found[0].what.startsWith(kind));
  }
});

Deno.test("without a blame answer nothing is claimed, because staleness cannot be shown", async () => {
  const file = { path: "a/Foo.cs", text: "// TODO: revisit\n" };
  assertEquals((await staleTodo.run(ctx([file]))).length, 0);
});

Deno.test("the age threshold is the boundary it says it is", async () => {
  const check = makeStaleTodo(10);
  const file = { path: "a/F.cs", text: "// TODO: x" };
  const nine = new Date(NOW.getTime() - 9 * 86_400_000).toISOString();
  const eleven = new Date(NOW.getTime() - 11 * 86_400_000).toISOString();
  assertEquals((await check.run(ctx([file], { blame: blameAt(nine) }))).length, 0);
  assertEquals((await check.run(ctx([file], { blame: blameAt(eleven) }))).length, 1);
});

// --- doc-names-missing-file -------------------------------------------------------------------

Deno.test("looksLikeRepoPath admits paths and rejects everything ambiguous", () => {
  for (const p of ["libs/a/boot.js", "docs/specs/012-htmx-4-upgrade/012-plan.md", "./a/b.cs"]) {
    assert(looksLikeRepoPath(p), `expected a path: ${p}`);
  }
  for (
    const p of [
      "https://example.com/a.md", //  a URL
      "#a-heading", //                an anchor
      "/settings/sessions", //        an application route
      "hx-swap", //                   an attribute name
      "boot.js", //                   bare filename, too ambiguous to claim
      "apps/**/*.cs", //              a glob
      "some words/here.cs", //        prose
      "dist/htmx.min.js", //          another project's build output
      "node_modules/x/index.js", //   ditto
    ]
  ) {
    assert(!looksLikeRepoPath(p), `expected not a path: ${p}`);
  }
});

Deno.test("cleanPath drops an anchor and a leading ./", () => {
  assertEquals(cleanPath("./docs/a.md#section"), "docs/a.md");
});

Deno.test("resolutions offers both the repo-relative and document-relative readings", () => {
  assertEquals(resolutions("docs/specs/012/012-plan.md", "./012-spec.md"), [
    "012-spec.md",
    "docs/specs/012/012-spec.md",
  ]);
  assertEquals(resolutions("docs/a/b.md", "../c/d.md"), ["c/d.md", "docs/c/d.md"]);
});

Deno.test("a document-relative link that resolves is not reported", async () => {
  const file = {
    path: "docs/specs/012/012-plan.md",
    text: "Spec: [here](./012-spec.md) and `libs/a/boot.js`.",
  };
  const found = await docNamesMissingFile.run(
    ctx([file], { allPaths: new Set(["docs/specs/012/012-spec.md", "libs/a/boot.js"]) }),
  );
  assertEquals(found, []);
});

Deno.test("a path written relative to an app root resolves against the tracked tree", () => {
  // Measured: 9 of the first 12 real findings were this shape, and every one was a false
  // positive. Documentation says `Render/Layout.cs` because that is what the reader sees once
  // they are inside apps/web.
  const tracked = new Set(["apps/web/Render/Layout.cs", "apps/web/wwwroot/vendor/nativeweb/boot.js"]);
  assert(resolvesAnywhere("docs/a.md", "Render/Layout.cs", tracked));
  assert(resolvesAnywhere("docs/a.md", "wwwroot/vendor/nativeweb/boot.js", tracked));
  assert(!resolvesAnywhere("docs/a.md", "Render/Gone.cs", tracked));
  // A suffix must land on a segment boundary, not mid-name.
  assert(!resolvesAnywhere("docs/a.md", "ender/Layout.cs", tracked));
});

Deno.test("a home-relative path is outside the repository and is never a finding", async () => {
  const file = { path: "work/todo.md", text: "See `~/.claude/away-questions.md` for the list." };
  assertEquals((await docNamesMissingFile.run(ctx([file]))).length, 0);
  assertEquals(looksLikeRepoPath("~/.claude/away-questions.md"), false);
});

Deno.test("a path no tracked file matches is reported once, with a grep to confirm it", async () => {
  const file = {
    path: "docs/a.md",
    text: "See `libs/gone/old.cs` for detail.\nAlso `libs/gone/old.cs` again.\n",
  };
  const found = await docNamesMissingFile.run(ctx([file], { allPaths: new Set(["libs/here/new.cs"]) }));
  assertEquals(found.length, 1, "the same missing path twice is one finding");
  assertEquals(found[0].line, 1);
  assert(found[0].confirm.includes("git ls-files"));
  assertEquals(validate(found[0]), null);
});

Deno.test("a non-Markdown file in the slice is not scanned for documentation paths", async () => {
  const file = { path: "apps/web/Program.cs", text: 'var s = "libs/gone/old.cs";' };
  assertEquals((await docNamesMissingFile.run(ctx([file]))).length, 0);
});

Deno.test("a URL in a link target is never a missing file", async () => {
  const file = { path: "docs/a.md", text: "[htmx](https://four.htmx.org/announcements/x.md)" };
  assertEquals((await docNamesMissingFile.run(ctx([file]))).length, 0);
});

// --- assertionless-test ------------------------------------------------------------------------

Deno.test("stripInert removes comments and string literals", () => {
  assertEquals(stripInert('// Assert.Equal(1, 2)\nvar s = "Assert.True(x)";').includes("Assert."), false);
});

Deno.test("a // inside a string does not eat the rest of the line", () => {
  // Regression. The first version stripped comments before strings, so this line lost its
  // closing brace and every block after it in the file was matched against the wrong text.
  const source = 'const file = { path: "a/F.cs", text: "// TODO: x" };';
  const stripped = stripInert(source);
  assertEquals(stripped.length, source.length, "offsets must survive stripping");
  assertEquals((stripped.match(/\{/g) ?? []).length, 1);
  assertEquals((stripped.match(/\}/g) ?? []).length, 1);
});

Deno.test("stripping preserves line numbers across a multi-line comment", () => {
  const source = "a\n/* x\n y */\nb";
  assertEquals(lineAt(stripInert(source), stripInert(source).indexOf("b")), 4);
});

Deno.test("a test whose only assertion is inside a string containing // is still seen", async () => {
  const text = 'Deno.test("x", () => {\n  const s = "// not a comment";\n  assertEquals(s.length, 17);\n});';
  assertEquals((await assertionlessTest.run(ctx([{ path: "a.test.ts", text }]))).length, 0);
});

Deno.test("a C# verbatim string containing a quote does not unbalance the scanner", () => {
  const source = 'var s = @"he said ""hi"" and { left this";\nvoid M() { }';
  const stripped = stripInert(source);
  assertEquals((stripped.match(/\{/g) ?? []).length, 1);
});

Deno.test("braceBody returns the matching block, not the first close brace", () => {
  const block = braceBody("void M() { if (a) { b(); } c(); }", 0);
  assertEquals(block?.body.trim(), "if (a) { b(); } c();");
});

Deno.test("an xUnit fact with no assertion is reported", async () => {
  const text = `public class T {
    [Fact]
    public void Does_a_thing() {
        var sut = new Thing();
        sut.Run();
    }
}`;
  const found = await assertionlessTest.run(ctx([{ path: "tests/web/T.cs", text }]));
  assertEquals(found.length, 1);
  assertEquals(found[0].line, 2);
  assert(found[0].what.includes("Does_a_thing"));
  assertEquals(validate(found[0]), null);
});

Deno.test("an xUnit fact that asserts, in any of the supported styles, is not reported", async () => {
  const bodies = [
    "Assert.Equal(1, sut.X);",
    "sut.X.Should().Be(1);",
    "mock.Verify(m => m.Run());",
    "await Expect(page.Locator(\"h1\")).ToHaveTextAsync(\"x\");",
    "Assert.Throws<InvalidOperationException>(() => sut.Run());",
  ];
  for (const body of bodies) {
    const text = `public class T {\n  [Fact]\n  public void M() {\n    ${body}\n  }\n}`;
    const found = await assertionlessTest.run(ctx([{ path: "tests/web/T.cs", text }]));
    assertEquals(found.length, 0, `should count as an assertion: ${body}`);
  }
});

Deno.test("an assertion that only appears in a comment does not count", async () => {
  const text = `public class T {
    [Fact]
    public void M() {
        // Assert.Equal(1, 2) -- removed while debugging
        Run();
    }
}`;
  assertEquals((await assertionlessTest.run(ctx([{ path: "tests/T.cs", text }]))).length, 1);
});

Deno.test("a Theory is treated the same as a Fact", async () => {
  const text = `public class T {\n  [Theory]\n  [InlineData(1)]\n  public void M(int a) {\n    Run(a);\n  }\n}`;
  assertEquals((await assertionlessTest.run(ctx([{ path: "tests/T.cs", text }]))).length, 1);
});

Deno.test("a Deno.test case with no assert is reported", async () => {
  const text = `Deno.test("does a thing", () => {\n  const x = compute();\n  console.log(x);\n});`;
  const found = await assertionlessTest.run(ctx([{ path: "tools/audit/a.test.ts", text }]));
  assertEquals(found.length, 1);
  assertEquals(found[0].line, 1);
});

Deno.test("a Deno.test case that asserts is not reported", async () => {
  const text = `Deno.test("x", () => {\n  assertEquals(compute(), 1);\n});`;
  assertEquals((await assertionlessTest.run(ctx([{ path: "a.test.ts", text }]))).length, 0);
});

Deno.test("production code and non-test files are not scanned", async () => {
  const text = `public class Service {\n  public void Run() {\n    Work();\n  }\n}`;
  assertEquals((await assertionlessTest.run(ctx([{ path: "apps/web/Service.cs", text }]))).length, 0);
});

Deno.test("this repository's own audit tests contain no assertionless case", async () => {
  const paths = ["ledger.test.ts", "redact.test.ts", "slices.test.ts", "checks/checks.test.ts"];
  const files: SourceFile[] = [];
  for (const p of paths) {
    files.push({ path: `tools/audit/${p}`, text: await Deno.readTextFile(new URL(`../${p}`, import.meta.url)) });
  }
  const found = await assertionlessTest.run(ctx(files));
  assertEquals(found.map((f) => `${f.path}:${f.line}`), [], "the audit's own tests must assert");
});
