# 001 — DevMarks: tasks

Task breakdown for `001-spec.md` per `001-plan.md`. One task per implementation slice; each is a
branch off `dev`, ships its own unit + integration (and e2e where flows are added) coverage, and
merges back `--no-ff` when green. Requirement numbers refer to the spec.

- [x] T1 Scaffold — projects (`apps/devmarks`, `libs/nativeweb.devmarks.data`, three test
      projects), solution entries, `db/devmarks/` migrations 0001–0005 + migrate.sh + schema
      snapshot, middleware/layout/CSS/vendored assets, demo bookmarks list, CI additions.
      Done when: all three test tiers green, AOT publish zero-warning, visible app delivered.
- [ ] T2 Accounts — register / sign-in / sign-out / settings, DB-backed sessions, lockout
      (reused `LockoutPolicy`), breach-checked passphrases; real no-JS forms (R1–R4, R24).
      Done when: full account lifecycle passes all three tiers, JS disabled included.
- [x] T3 Bookmarks core — CRUD, canonical-URL dedup-on-save, list + pagination, demo data
      removed (R5, R8, R9, R13). Done when: CRUD flows pass all three tiers against Postgres.
- [x] T4 Metadata fetch — SSRF-guarded fetcher, title/description/favicon, favicon endpoint
      (R6, R7). Done when: guard unit tests (incl. redirect re-checks) and fetch flow pass.
- [x] T5 Tags — implicit create, rename/merge, delete, counts, filter (R10, R11).
      Done when: tag flows pass unit + integration.
- [x] T6 Search — Postgres FTS via the expression GIN index, ranking, combined with tag filter
      (R14). Done when: ranked search integration tests pass and EXPLAIN uses the index.
- [x] T7 Public pages — visibility flags, `/u/{username}` (+ per-tag), 404-indistinguishability
      (R12, R15–R17). Done when: anonymous flows pass all three tiers.
- [x] T8 Import/export — Netscape HTML + JSON codecs, idempotent import with report, lossless
      JSON round-trip (R18–R20). Done when: round-trip property holds in tests.
- [x] T9 Hardening — rate limiters, request logging, no-JS sweep, small screens, README, full
      acceptance-criteria sweep (R21–R24). Done when: every spec acceptance criterion checked
      off and the full suite is green. (Formatted-HTML-source retrofit dropped from scope
      2026-07-30 — cosmetic, not an acceptance criterion; tracked in work/todo.md.)
