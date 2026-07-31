# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V3 (spec 003)** — `feat/i18n` (merged e3f5c01): locale catalogs as AdditionalFiles with
  build-time dialect fallthrough (en root → base language → dialect, complete per-locale
  tables in a generated `Locales` class), `{{t:}}` keys compile-checked (NWT013–NWT015),
  implicit `LocaleCatalog locale` param threaded transitively, LocaleNegotiation middleware
  (pref → cookie → Accept-Language → default), migration 0053 `users.locale`, and the R45
  mail refactor: nativeweb.mail transport-only, `LocalizedEmailRenderer` renders in the
  recipient's stored locale. Opus review: 17 findings, 10 fixed in-branch (+11 regression
  tests), 6 deferred to work/todo.md. 75 render tests + all existing suites green.
