# 002 — Open asks from NativeWeb to JauntyQ

Created: 2026-08-30
For: the JauntyQ maintainer. Nothing here is a NativeWeb task; this document exists so the whole
batch can be handed over at once instead of leaking into this repo's backlog one item at a time.

Everything below was found while building NativeWeb against a local JauntyQ checkout. Each item
says what was observed, what was measured, and what NativeWeb would like — separated, so the ask
can be refused without the observation being lost.

---

## 1. The generator drops its entire output on incremental rebuild, intermittently

**Observed twice on 2026-08-17**, both times in `libs/nativeweb.auth`. The whole
`JauntyQ.Generated` namespace vanished, so every file in the project failed `CS0246` — including
files the edit had not touched. A clean rebuild fixed it both times and nothing was wrong with the
source.

**Why it is worse than a flake.** The failure mode is indistinguishable from "I broke the build,
badly". The second occurrence buried one genuine compile error under about forty spurious ones,
and the instinct is to start fixing code that was never broken.

**Not diagnosed here, and cannot be.** Reproducing it means catching the flake with binlogs on,
which nothing in NativeWeb does routinely, and the generator lives in the JauntyQ checkout.
NativeWeb's own mitigation is a note in `work/todo.md`: if the whole namespace disappears at once,
rebuild clean before believing the errors.

**Ask:** whether incremental-generator caching is expected to be able to evict a whole output set,
and if so, whether it can be made to fail loudly rather than emit nothing.

## 2. Nothing in either repo can detect packaged-generator skew

**Measured 2026-08-17.** JauntyQ 0.1.0 predated migration-parser index-DDL support. NativeWeb
declares its whole schema in migration DDL, so the packaged generator saw every index as absent
and raised `JNT8004 No index covers ...` across the repo. The local checkout was **1,079 commits
past the tag and still stamped `<Version>0.1.0</Version>`**, identical to the published package,
so no version string distinguished them.

**The structural point, which outlives that specific bug:** the packaged generator's diagnostics
fire in the *consumer's* compilation, not in JauntyQ's own suite. **A green JauntyQ build says
nothing about whether consumers compile.** Closed for now — 0.2.0 shipped, the pins moved, and the
solution builds 0 warnings / 0 errors in package mode — but the detection gap is unchanged.

**Ask:** a consumer-compilation smoke job in JauntyQ's CI, and a version stamp on the local
checkout that cannot be confused with a released one (a `-dev` suffix or the commit height).

## 3. The migration runner records outside the transaction it applies in

Each migration's `schema_migrations` row is written after the apply, not inside it. For pure DDL
that is what every migration before NativeWeb's `0054` relied on; for a data `UPDATE` it is not
harmless — an interrupted run can leave the data changed and the row absent, or the reverse.
`0054` works around it by writing its own `schema_migrations` row inside its own transaction.

**Ask:** have the runner record inside the transaction it applies in. That is a change to how it
invokes `psql`, not to the SQL. Noted 2026-08-16.

## 4. Multi-result-set batching — status check, not a re-ask

Deferred upstream at JauntyQ `80af756` (2026-08-03) and recorded in that repo's `work/todo.md`
under "Later". Restated here only so the batch is complete: the original motivation was **round
trips, not query cost** (`docs/jauntyq/2026-08-02-001-result-set-features.md`).

The window-function polish that *did* land is in use and working: `COUNT(...) OVER (...)` infers
`bigint NOT NULL`, so `-- @type` is no longer needed for the paginate-with-total shape, and
`JNT1008` makes a two-statement file an error rather than a silent mis-parse. All three consumers
build 0/0 against it, verified 2026-08-03.

## 5. Q7 — the joint asks from the framework brainstorm

From `docs/brainstorms/001-framework-direction.md` §10, batched here as that document intended:

- **Scope-predicate support**, or a ruling that row-level security is the answer. NativeWeb's
  `[ScopedBy]` design is layered — portable generator enforcement as the guarantee, RLS as an
  optional deployment-time backstop on the dialects that have it — and the generator half is
  JauntyQ's to say yes or no to.
- **The model-to-query naming convention**: whether JauntyQ wants to own one, so a scaffolding
  generator can find the queries belonging to a model without a per-consumer convention.
- **Eager loading / `select_related`** — is it on the roadmap? NativeWeb has ruled out building a
  fluent query builder over JauntyQ (that would be rewriting the ORM, and SQL-first is JauntyQ's
  thesis), so if relationship loading is wanted it is JauntyQ's job or nobody's.

## 6. Not a JauntyQ bug, listed so it is not mistaken for one

`extrode/nativeweb` CI cannot restore `JauntyQ.Runtime`, `JauntyQ.Generator` or
`JauntyQ.Schema.Contract`. The packages exist and are `private`, owned by `extrode/jauntyq`; the
job's `GITHUB_TOKEN` is scoped to its own repository, so the restore returns **403, not 404**. The
fix is an org-side package grant (Manage Actions access → `extrode/nativeweb` → Read), not a
change to either codebase. Recorded here only because a 403 during restore reads like a JauntyQ
publishing fault and is not one.
