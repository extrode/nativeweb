// Documentation naming a file that is not in the repository.
//
// This is the cheapest true defect an audit can find, and one of the most corrosive to leave
// standing: a reader who follows a path that does not exist stops trusting the document, and
// stops checking the paths that do. Spec 012 hit this from the other side — prose on a branch
// asserting something the code did not do — and it took a review to catch.
//
// The rule is deliberately narrow. It fires only on a string that is unmistakably a repository
// path: it contains a slash, ends in a known source extension, and is not a URL. Everything
// vaguer is left alone, because a false positive here trains the reader to skim the digest.

import type { Candidate } from "../ledger.ts";
import { type Check, type CheckContext, excerptAt, lineAt } from "./types.ts";
import { SOURCE_EXTENSIONS } from "../slices.ts";

/** Inline code spans and Markdown link targets: the two places a path is written on purpose. */
const CANDIDATE_SPAN = /`([^`\n]+)`|\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;

const EXT = new RegExp(`(${SOURCE_EXTENSIONS.map((e) => e.replace(".", "\\.")).join("|")})$`, "i");

export function looksLikeRepoPath(raw: string): boolean {
  const value = raw.trim();
  if (value.length === 0 || value.length > 200) return false;
  if (/^[a-z][a-z0-9+.-]*:/i.test(value)) return false; // http:, mailto:, file:
  if (value.startsWith("#")) return false; // an in-page anchor
  if (value.includes(" ")) return false; // prose, not a path
  if (value.startsWith("/")) return false; // a URL route, not a repository path
  if (!value.includes("/")) return false; // a bare filename is too ambiguous to claim
  if (/[*?<>|]/.test(value)) return false; // a glob or a placeholder
  return EXT.test(value.replace(/[#?].*$/, ""));
}

/** Strip an anchor or query, and resolve a leading `./`. */
export function cleanPath(raw: string): string {
  return raw.trim().replace(/[#?].*$/, "").replace(/^\.\//, "").replaceAll("\\", "/");
}

/**
 * Resolve a path written inside `fromDoc`. Documentation writes both repository-relative paths
 * (`libs/nativeweb.assets/boot.js`) and document-relative ones (`./012-plan.md`), and only the
 * writer knows which; so a path is a defect only when neither reading exists.
 */
export function resolutions(fromDoc: string, path: string): string[] {
  const dir = fromDoc.includes("/") ? fromDoc.slice(0, fromDoc.lastIndexOf("/")) : "";
  const relative = dir.length > 0 ? `${dir}/${path}` : path;
  const collapse = (p: string) => {
    const out: string[] = [];
    for (const seg of p.split("/")) {
      if (seg === "." || seg === "") continue;
      if (seg === "..") out.pop();
      else out.push(seg);
    }
    return out.join("/");
  };
  return [...new Set([collapse(path), collapse(relative)])];
}

export const docNamesMissingFile: Check = {
  ruleId: "doc-names-missing-file",
  describe: "A Markdown document names a repository path that no tracked file matches.",
  run(ctx: CheckContext): Promise<Candidate[]> {
    const found: Candidate[] = [];
    for (const file of ctx.files) {
      if (!file.path.toLowerCase().endsWith(".md")) continue;
      const re = new RegExp(CANDIDATE_SPAN.source, "g");
      let m: RegExpExecArray | null;
      const reported = new Set<string>();
      while ((m = re.exec(file.text)) !== null) {
        const raw = m[1] ?? m[2];
        if (raw === undefined || !looksLikeRepoPath(raw)) continue;
        const path = cleanPath(raw);
        if (reported.has(path)) continue;
        if (resolutions(file.path, path).some((r) => ctx.allPaths.has(r))) continue;
        reported.add(path);

        const line = lineAt(file.text, m.index);
        found.push({
          ruleId: "doc-names-missing-file",
          path: file.path,
          line,
          what: `Names \`${path}\`, which no tracked file matches.`,
          why:
            "A reader who follows this path finds nothing, and thereafter checks none of the " +
            "other paths in this document. Either the file moved and the reference was missed, " +
            "or the document describes something that was never built.",
          confirm: `git ls-files | grep -F '${path}'  # expected: at least one match`,
          excerpt: excerptAt(file.text, line, 1),
          verified: "verified",
        });
      }
    }
    return Promise.resolve(found);
  },
};
