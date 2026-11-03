/**
 * Prints one version's section of CHANGELOG.md, the body of its GitHub Release.
 *
 *   deno run -A tools/release-notes.ts 0.1.0 > notes.md
 *
 * The section is everything under `## [0.1.0]` up to the next `## [` heading or a `---` rule,
 * with the heading itself left out: the release already carries the tag as its title. A version
 * with no section, or an empty one, is an error rather than an empty release.
 *
 * GitHub refuses a release body over 125,000 characters, and 0.1.0's section is 161,136: the
 * whole pre-release history. A section that long is cut before the bullet that would cross the
 * limit and ends with a link to the full CHANGELOG.md at the tag.
 */

export const BODY_LIMIT = 125_000;

export function releaseNotes(changelog: string, version: string): string {
  const lines = changelog.split(/\r?\n/);
  const heading = `## [${version}]`;
  const start = lines.findIndex((line) => line === heading || line.startsWith(`${heading} `));

  if (start < 0) {
    throw new Error(`CHANGELOG.md has no "${heading}" section.`);
  }

  const rest = lines.slice(start + 1);
  const end = rest.findIndex((line) => line.startsWith("## [") || line.trim() === "---");
  const body = (end < 0 ? rest : rest.slice(0, end)).join("\n").trim();

  if (body.length === 0) {
    throw new Error(`CHANGELOG.md's "${heading}" section is empty.`);
  }

  return fitted(`${body}\n`, version);
}

function fitted(notes: string, version: string): string {
  const link = "\n_Cut to fit a GitHub release. The full notes are in " +
    `[CHANGELOG.md](https://github.com/extrode/nativeweb/blob/v${version}/CHANGELOG.md)._\n`;

  if (notes.length <= BODY_LIMIT) return notes;

  const lines = notes.split("\n");
  let kept = 0;
  let length = 0;
  let cut = 0;

  for (const [index, line] of lines.entries()) {
    if (length + line.length + 1 + link.length > BODY_LIMIT) break;
    length += line.length + 1;
    kept = index + 1;
    if (/^(- |### )/.test(lines[index + 1] ?? "")) cut = kept;
  }

  return `${lines.slice(0, cut || kept).join("\n").trimEnd()}\n${link}`;
}

if (import.meta.main) {
  const [version, path = "CHANGELOG.md"] = Deno.args;

  if (!version) {
    console.error("usage: deno run -A tools/release-notes.ts <version> [changelog]");
    Deno.exit(2);
  }

  try {
    Deno.stdout.writeSync(new TextEncoder().encode(releaseNotes(await Deno.readTextFile(path), version)));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    Deno.exit(1);
  }
}
