# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V8 (spec 003)** — `feat/static-pages` (merged into `dev`): `wwwroot/pages/*.html` at
  clean URLs via a path rewrite before an explicit `UseRouting`, MapStaticAssets doing the
  serving (ETag + no-cache revalidation, 304s, precompressed at publish); nonce-free strict
  CSP branch (`default-src 'none'`, `form-action 'none'`) keyed off an Items marker; error
  pages in BOTH apps render from a startup-loaded static shell (devmarks gained its first
  ErrorPageMiddleware), explicit no-store; robots.txt + sitemap.xml. Review fixes: the
  no-store made unconditional in WriteErrorAsync, a startup guard so a pages/ file can
  never shadow a mapped route (wiring proven non-vacuous), Load testability, CSP asserted
  as literals, compression×static seam tested (Vary confirmed), twin test parity. Suites
  green (web 349/236/49, devmarks 217/81/12); axe passes /about and the static 404.
