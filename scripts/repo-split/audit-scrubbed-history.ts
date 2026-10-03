// Audits a repository's history against the private scrub rules before it goes public.
//
//   deno run -A scripts/repo-split/audit-scrubbed-history.ts --rules-dir <dir> [--repo <path>]
//        [--ref dev] [--tree]
//
// Fails (exit 1) on any path from the remove lists, any match of a text rule in a blob, any match
// of a message rule in a commit message, or more than one author/committer identity, anywhere in
// the history reachable from --ref. --tree checks only the tip tree, which is what the prep branch
// drives to zero before the rewrite. Exit 2 means it could not run (missing rules, bad repo).
//
// The rules are not in this repository on purpose: they spell out what must not be in it. See
// docs/decisions/2026-10-03-015-fresh-public-repository.md.

export type Rule = { source: string; pattern: RegExp };

export type Rules = {
  paths: string[];
  text: Rule[];
  message: Rule[];
};

export type Hit = { where: string; rule: string; count: number };

export type Report = {
  pathHits: string[];
  blobHits: Hit[];
  messageHits: Hit[];
  identities: string[];
  markers: Map<string, number>;
};

const PATH_LISTS = ["paths-idp.txt", "paths-devmarks.txt", "paths-other.txt"];
const MARKERS = /WebAuthn|TwoFactor|OAuthToken|Consent|\brealm\b|ghosthive|justsmtp|manzil|another-product/g;

export function parsePathList(text: string): string[] {
  return text.split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith("#"));
}

// The format git filter-repo reads for --replace-text and --replace-message. It has no comment
// syntax, so a line that is not a rule is an error here rather than a silent literal there.
export function parseRules(text: string): Rule[] {
  return text.split(/\r?\n/).filter((l) => l.length > 0).map((line) => {
    const body = line.includes("==>") ? line.slice(0, line.lastIndexOf("==>")) : line;
    if (body.startsWith("regex:")) {
      return { source: body.slice(6), pattern: new RegExp(body.slice(6), "g") };
    }
    if (body.startsWith("#")) {
      throw new Error(`comment line in a rules file, which filter-repo would treat as a literal: ${line}`);
    }
    const literal = body.startsWith("literal:") ? body.slice(8) : body;
    return { source: literal, pattern: new RegExp(literal.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "g") };
  });
}

export async function loadRules(dir: string): Promise<Rules> {
  const read = (name: string) => Deno.readTextFile(`${dir}/${name}`);
  const paths: string[] = [];
  for (const name of PATH_LISTS) {
    paths.push(...parsePathList(await read(name)));
  }
  return {
    paths,
    text: parseRules(await read("text-rules.txt")),
    message: parseRules(await read("message-rules.txt")),
  };
}

export function isRemovedPath(path: string, removed: string[]): boolean {
  return removed.some((p) => path === p || path.startsWith(p + "/"));
}

export function matchRules(text: string, rules: Rule[], where: string): Hit[] {
  const hits: Hit[] = [];
  for (const rule of rules) {
    const count = text.match(rule.pattern)?.length ?? 0;
    if (count > 0) hits.push({ where, rule: rule.source, count });
  }
  return hits;
}

async function git(repo: string, args: string[], stdin?: Uint8Array): Promise<Uint8Array> {
  const cmd = new Deno.Command("git", {
    args: ["-C", repo, ...args],
    stdin: stdin ? "piped" : "null",
    stdout: "piped",
    stderr: "piped",
  });
  const child = cmd.spawn();
  if (stdin) {
    const writer = child.stdin.getWriter();
    await writer.write(stdin);
    await writer.close();
  }
  const out = await child.output();
  if (!out.success) {
    throw new Error(`git ${args.join(" ")}: ${new TextDecoder().decode(out.stderr).trim()}`);
  }
  return out.stdout;
}

const latin1 = new TextDecoder("latin1");
const lines = (b: Uint8Array) => latin1.decode(b).split("\n").filter((l) => l.length > 0);

// Every blob reachable from ref (or in its tip tree), with the first path git names it by.
async function blobsOf(repo: string, ref: string, tree: boolean): Promise<Map<string, string>> {
  const blobs = new Map<string, string>();
  if (tree) {
    for (const l of lines(await git(repo, ["ls-tree", "-r", ref]))) {
      const [meta, path] = l.split("\t");
      const [, type, sha] = meta.split(" ");
      if (type === "blob") blobs.set(sha, path);
    }
    return blobs;
  }
  const objects = lines(await git(repo, ["rev-list", "--objects", ref]));
  const shas = objects.map((l) => l.split(" ")[0]).join("\n") + "\n";
  const types = lines(await git(repo, ["cat-file", "--batch-check"], new TextEncoder().encode(shas)));
  types.forEach((t, i) => {
    const [sha, type] = t.split(" ");
    if (type === "blob" && !blobs.has(sha)) blobs.set(sha, objects[i].slice(41) || "?");
  });
  return blobs;
}

async function readBlobs(repo: string, shas: string[]): Promise<Uint8Array[]> {
  const raw = await git(repo, ["cat-file", "--batch"], new TextEncoder().encode(shas.join("\n") + "\n"));
  const out: Uint8Array[] = [];
  let at = 0;
  while (at < raw.length) {
    const nl = raw.indexOf(10, at);
    const size = Number(latin1.decode(raw.subarray(at, nl)).split(" ")[2]);
    out.push(raw.subarray(nl + 1, nl + 1 + size));
    at = nl + 1 + size + 1;
  }
  return out;
}

export async function audit(repo: string, rules: Rules, ref = "dev", tree = false): Promise<Report> {
  const report: Report = { pathHits: [], blobHits: [], messageHits: [], identities: [], markers: new Map() };

  const paths = tree
    ? lines(await git(repo, ["ls-tree", "-r", "--name-only", ref]))
    : lines(await git(repo, ["log", "--format=", "--name-only", ref]));
  report.pathHits = [...new Set(paths.filter((p) => isRemovedPath(p, rules.paths)))].sort();

  const blobs = await blobsOf(repo, ref, tree);
  const shas = [...blobs.keys()];
  const contents = await readBlobs(repo, shas);
  contents.forEach((bytes, i) => {
    const path = blobs.get(shas[i])!;
    const text = latin1.decode(bytes);
    report.blobHits.push(...matchRules(text, rules.text, `${path} (${shas[i].slice(0, 10)})`));
    const markers = text.match(MARKERS)?.length ?? 0;
    if (markers > 0) report.markers.set(path, (report.markers.get(path) ?? 0) + markers);
  });

  if (!tree) {
    const log = latin1.decode(await git(repo, ["log", "--format=%H%x00%B%x01", ref]));
    for (const record of log.split("\x01")) {
      const [sha, body] = record.replace(/^\n/, "").split("\x00");
      if (body) report.messageHits.push(...matchRules(body, rules.message, `commit ${sha.slice(0, 10)}`));
    }
  }

  report.identities = [
    ...new Set(lines(await git(repo, ["log", "--format=%an <%ae>%n%cn <%ce>", ref]))),
  ].sort();
  return report;
}

export function failures(report: Report): number {
  return report.pathHits.length + report.blobHits.length + report.messageHits.length +
    (report.identities.length > 1 ? 1 : 0);
}

export function parseArgs(args: string[]) {
  const opts = { repo: ".", ref: "dev", tree: false, rulesDir: "" };
  for (let i = 0; i < args.length; i++) {
    switch (args[i]) {
      case "--repo": opts.repo = args[++i]; break;
      case "--ref": opts.ref = args[++i]; break;
      case "--rules-dir": opts.rulesDir = args[++i]; break;
      case "--tree": opts.tree = true; break;
      default: throw new Error(`unknown argument: ${args[i]}`);
    }
  }
  if (!opts.rulesDir) throw new Error("--rules-dir is required; the rules are held outside the repository");
  return opts;
}

if (import.meta.main) {
  let opts, rules;
  try {
    opts = parseArgs(Deno.args);
    rules = await loadRules(opts.rulesDir);
  } catch (e) {
    console.error(`audit: ${(e as Error).message}`);
    Deno.exit(2);
  }
  const report = await audit(opts.repo, rules, opts.ref, opts.tree);
  for (const p of report.pathHits) console.log(`path     ${p}`);
  for (const h of report.blobHits) console.log(`blob     ${h.where}  ${h.rule} x${h.count}`);
  for (const h of report.messageHits) console.log(`message  ${h.where}  ${h.rule} x${h.count}`);
  if (report.identities.length > 1) console.log(`identity ${report.identities.join(" | ")}`);
  const top = [...report.markers].sort((a, b) => b[1] - a[1]).slice(0, 40);
  if (top.length) console.log(`\ninfo: IdP and sibling markers, top ${top.length} paths (not failures)`);
  for (const [path, n] of top) console.log(`  ${String(n).padStart(5)}  ${path}`);
  const n = failures(report);
  console.log(`\n${opts.tree ? "tip tree" : "history"} of ${opts.ref}: ${n === 0 ? "clean" : `${n} finding(s)`}`);
  Deno.exit(n === 0 ? 0 : 1);
}
