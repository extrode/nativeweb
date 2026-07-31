# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V2 (spec 003)** — `feat/render-engine` (merged 0134d9d): `libs/nativeweb.render` runtime
  (TemplateWriter over IBufferWriter<byte>, Html raw type, IResult/string adapters) +
  `libs/nativeweb.render.generator` (templates/**/*.html → static classes, u8 literals,
  encoded-by-default via overload resolution, layouts inlined with slots/fills, partials as
  method calls, NWT001–NWT012 build errors). Opus review: 20 findings, 14 fixed in-branch,
  6 design-scale ones deferred to work/todo.md. 46 render tests + all existing suites green.
