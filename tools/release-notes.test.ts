import { assertEquals, assertThrows } from "jsr:@std/assert@1";

import { BODY_LIMIT, releaseNotes } from "./release-notes.ts";

const changelog = [
  "# Changelog",
  "",
  "## [Unreleased]",
  "",
  "- next",
  "",
  "## [0.2.0] — 2026-12-01",
  "",
  "### Added",
  "",
  "- two",
  "",
  "## [0.1.0] — 2026-11-04",
  "",
  "- one",
  "",
  "---",
  "",
  "footer",
].join("\n");

Deno.test("releaseNotes returns the section between its heading and the next", () => {
  assertEquals(releaseNotes(changelog, "0.2.0"), "### Added\n\n- two\n");
});

Deno.test("releaseNotes stops the last section at a rule", () => {
  assertEquals(releaseNotes(changelog, "0.1.0"), "- one\n");
});

Deno.test("releaseNotes does not take 0.1.0 for 0.1.0-rc1 or 0.1.00", () => {
  assertThrows(
    () => releaseNotes("## [0.1.0-rc1] — x\n\n- rc\n## [0.1.00]\n\n- no\n", "0.1.0"),
    Error,
    "no \"## [0.1.0]\" section",
  );
});

Deno.test("releaseNotes refuses a version with no section", () => {
  assertThrows(() => releaseNotes(changelog, "9.9.9"), Error, "no \"## [9.9.9]\" section");
});

Deno.test("releaseNotes refuses an empty section", () => {
  assertThrows(() => releaseNotes("## [0.3.0]\n\n## [0.2.0]\n\n- two\n", "0.3.0"), Error, "is empty");
});

Deno.test("releaseNotes reads the real changelog's 0.1.0", async () => {
  const notes = releaseNotes(await Deno.readTextFile(new URL("../CHANGELOG.md", import.meta.url)), "0.1.0");

  assertEquals(notes.startsWith("### "), true);
});

Deno.test("releaseNotes cuts an oversized section before a bullet and links the full changelog", () => {
  const bullet = `- ${"x".repeat(98)}\n  continued\n`;
  const long = `## [1.0.0]\n\n${bullet.repeat(Math.ceil(BODY_LIMIT / bullet.length) + 10)}`;

  const notes = releaseNotes(long, "1.0.0");

  assertEquals(notes.length <= BODY_LIMIT, true);
  assertEquals(notes.includes("blob/v1.0.0/CHANGELOG.md"), true);
  assertEquals(notes.split("\n_Cut")[0].trimEnd().endsWith("  continued"), true);
});

Deno.test("releaseNotes leaves a section under the limit whole", () => {
  assertEquals(releaseNotes(changelog, "0.2.0").includes("_Cut"), false);
});
