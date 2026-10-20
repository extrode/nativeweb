# 008 — Mutation-driven test strategy for the paging seam

Branch: `test/018-paging-property-tests`
Prompted by: `~/Downloads/testing-strategy-handover.md` (solver and fuzz-augmented testing),
after spec 017 shipped eight defects that no test in the repo failed on.

## The problem, stated as a measurement rather than a feeling

Spec 017 added sorting and keyset pagination to the scaffold list seam. Eight defects were
found in it — two while writing the unit tier, six by a two-lens code review of the finished
branch — and **not one of them failed a test**. Every line involved was executed. Line
coverage was not the missing thing.

The handover doc's section 1 gate says not to add a new testing method to an existing module
until a mutation run shows a real gap. Stryker.NET 4.16.0 was run first, before anything was
written:

| Target | Score | Killed | Survived | Uncovered |
| --- | --- | --- | --- | --- |
| `libs/nativeweb.scaffold`, unit tier (165 tests) | 75.46% | 123 | 10 | 30 |
| `apps/sample`, unit tier (66 tests) | 22.22% | 74 | 14 | 252 |
| `apps/sample`, integration tier (81 tests) | 44.74% | 136 | 116 | 73 |

`apps/sample`'s low figures are dominated by `Program.cs`, which no unit test can reach; the
file that matters, `DemoStore.cs`, scored **70.0%** at the unit tier.

The run named the gap precisely:

| Where | Mutant that survived | What no test did |
| --- | --- | --- |
| `ScaffoldAccess.cs` | all 15, uncovered | Nothing executed `CanReadAsync`. Its two tests assert the *generated source contains the call* — a substring match over a file. Five branches of an authorization decision, never run. |
| `DemoStore.cs:90` | `at < 0` → `at <= 0` | Nothing paged forward from the first row. |
| `DemoStore.cs:106` | `at - start` → `at + start` | The one backward test had `at == take`, so `start` was 0 and both arms computed the same number. |
| `DemoStore.cs:197` | `\|\|` → `&&` | Nothing passed a search term with an empty search-field list. |
| `ScaffoldSort.cs` ×4 | boundary and branch mutants in `TryDecode` | The truncated-prefix, overrun-length and empty-key paths were unreachable from an example. |
| `ScaffoldValues.cs:75` | `AdjustToUniversal \| AssumeUniversal` → `&` | Nothing asserted `NullableDateTime` reads an offset-less value as UTC. |

## Approach

Route each gap by the handover's section 3 table rather than reaching for one technique.

- **`ScaffoldAccess` gets example tests.** A human can enumerate all seven cases in one
  sitting, which is section 1's criterion 2 saying *don't* use machinery. Sixteen tests,
  driving a `DefaultHttpContext` against a stub `IAuthorizationService`.
- **The cursor codec gets property tests.** It is a hand-written binary parser on
  query-string input: section 3 routes that to fuzzing or property testing, and CsCheck's
  generators plus shrinking reach the truncation branches without a corpus to maintain.
- **The paging seam gets a model-based property test.** State-machine and combinatorial, not
  arithmetic, so section 3 says property testing rather than Z3. The oracle is an independent
  model of the order, written from the contract `IScaffoldStore<T>` states and never
  consulting the store's comparator.
- **Not adopted:** SharpFuzz (the cursor's input space is small enough for a generator, and a
  corpus is ongoing cost for a parser with three fields), Z3 (no interacting numeric
  constraints here), Testcontainers differential testing (the repo already runs the
  integration tier against real PostgreSQL, and the memory store is the differential oracle).

CsCheck 4.8.0 over FsCheck: plain C# API, no `FSharp.Core` dependency, test projects only, no
effect on the NativeAOT publish.

## Changes by area

| Path | What |
| --- | --- |
| `Directory.Packages.props` | `CsCheck` 4.8.0, in the test-only group |
| `tests/nativeweb.scaffold.tests.unit/ScaffoldAccessTests.cs` | new — 16 tests over `CanReadAsync` |
| `tests/nativeweb.scaffold.tests.unit/ScaffoldCursorPropertyTests.cs` | new — round trip, tamper, truncate, oversize, and the byte-level refusals |
| `tests/nativeweb.scaffold.tests.unit/ScaffoldValuesTests.cs` | three datetime assertions |
| `tests/nativeweb.sample.tests.unit/MemoryStorePagingPropertyTests.cs` | new — 11 properties against an independent order model |
| `tests/nativeweb.sample.tests.unit/TagStoreTests.cs` | new — the second `MemoryStore<T>`, whose key is not `Id` |
| `libs/nativeweb.scaffold/ScaffoldSort.cs` | strict UTF-8 in `TryRead` (below) |
| `tools/mutation-triage.ts` | re-runnable triage, comparing against a committed baseline |
| `tools/mutation-baseline.json` | the recorded figures |

## The one production change, and why a test caused it

`ATamperedTokenIsRejectedRatherThanThrowing` failed on its first run. `Encoding.UTF8.GetString`
substitutes U+FFFD for a malformed sequence rather than refusing it, so flipping one character
of a token produced a cursor that `TryDecode` **accepted** while its documented contract says
it returns true only for "a cursor this release wrote". The substitution is lossy, so the
token no longer re-encodes to itself.

`TryRead` now uses `Utf8.ToUtf16` with `replaceInvalidSequences: false` and refuses on anything
but `OperationStatus.Done`. Consequence for a reader: none that was not already there — an
edited cursor rendered the first page before and renders it now. What changed is that the
acceptance is exact, which is what makes the round-trip property assertable at all.

## Risks

- **Property tests can be vacuous in a way examples cannot**: a generator that never produces
  the interesting shape passes for the wrong reason. Mitigated by killing four named mutants
  by hand before trusting the suite, and by the score delta below.
- **Iteration count is a wall-clock cost.** 500 per paging property, 2,000 per cursor property;
  both unit suites still finish in under 7 s.
- **A mutation score moves with unrelated refactors.** `tools/mutation-triage.ts` is therefore
  a manual per-change tool with a 1-point tolerance, not a CI job — matching the handover's
  section 5, which puts full mutation runs in the nightly tier at most.

## Coverage check

| Gap from the triage | Closed by |
| --- | --- |
| `ScaffoldAccess`, 15 mutants | `ScaffoldAccessTests` |
| `DemoStore:90`, `:106`, `:197`, the tail-trim reset | `MemoryStorePagingPropertyTests`, each confirmed by hand-applying the mutant |
| `ScaffoldSort` decoder branches | `ScaffoldCursorPropertyTests` |
| `ScaffoldValues:75` | `NullableDateTime_*` |
| `DemoStore:250`, `:253` | `TagStoreTests` |

## What the review caught that the mutation score did not

An independent review of the branch found **two property tests that passed for the wrong
reason** — and neither showed up as a surviving mutant, because both did kill their mutants
while asserting less than their names claimed.

- `ABackwardCursorOnAMissingRowLeavesRoomForTheCallersTailTrim` asserted
  `page.Count <= Take - 1` and `page == model.Take(page.Count)`. An empty page satisfies both
  unconditionally, and `Take == 1` forced an empty page in a fifth of iterations. Now asserts
  the exact expected page.
- `ABackwardWalkFromTheEndVisitsEveryRowOnceInModelOrder` appended `model[^1].Id` to the
  observed list before comparing against the model, so its last position compared the oracle
  with itself; a single-row model reduced the whole test to `x == x`. Now walks to
  `model[0..^1]` and compares that.

Three more: the index `pick % model.Count` was drawn from `Gen.Int[0, 20]`, which sampled low
indices twice as often as high ones — and the **last row** is where two of the three 017
defects lived, so the least-sampled case was the one that mattered. Widened to
`Gen.Int[0, 1_000]`. `AnUnsortableTakeOfZeroReturnsNothing` never generated an unsortable
property, so it was renamed to what it asserts and a real fallback property added. And the
missing-row cursors were built from the constructor with a raw `"gone"` rather than through
`ScaffoldValues.CursorText`, feeding the store a shape the framework would never issue.

**The score did not move for any of this** — `sample-unit` stayed at 24.56%, the same mutants
dead. That is the honest limit of a mutation score: it measures whether a test *can* fail, not
whether it fails for the reason its name gives. Both checks are needed, and neither substitutes
for the other.

## The triage tool broke a test, and the test it broke deserved to break

The full-solution run on this branch came back with one failure the identical run before it did
not have: `PublicFeedTests.Feed_UnderNativeAot_OrdersNewestFirst` in the e2e tier, which nothing
on this branch touches.

The chain, each link measured rather than inferred:

1. `tools/mutation-triage.ts`'s `sample-integration` target ran 15:51–15:57 (`tmp/stryker/
   sample-integration` mtime). Stryker kills a test host it judges hung — 39 timeouts in that
   run — and a killed process never reaches the `finally` that removes its fixture rows.
2. Five future-dated published rows survived in `nativeweb_sample_test`, from three of the
   integration tier's four `feed-` prefixed tests. `GET /articles` shows the newest twenty, so
   they sat at the top of the public feed.
3. The leaked `feed-…-newer` is dated `future + 1h`. The e2e test dates its own pair `future`
   and `future + 1h` from its own `UtcNow`, so for **the hour after a leaking run** the leaked
   row lands between them. The suite ran inside that window; a re-run at 17:10 passed.

**The e2e assertion was the defect, not the rows.** It read `titles.First` and `titles.Nth(1)` —
absolute DOM slots, which is a claim about every other row in `articles`. The integration tier
asserting the same fact uses relative positions and was unaffected by the same rows. The e2e
test now finds its two titles in the rendered list and asserts one is above the other, keeping
the reason this tier exists (a real HTML parse of the NativeAOT binary's output) without the
claim it could not support. Its failure messages print the titles that were on the page, because
the original failure said only that slot 1 held the wrong text.

Two things follow. `tools/mutation-triage.ts` documents the residue in its header, since a tool
that leaves live state behind and does not say so is how this cost an afternoon. And
`scripts/cleanup/018-paging-property-tests.ps1` lists the rows behind `-DropTestRows`, scoped to
future-dated `feed-`/`e2e-` ids so a demo row cannot be caught by it. It has not been run.

**Left open, recorded rather than chased.** Four survivors are equivalent mutants — a change
with no observable difference, so no test can kill them: `ScaffoldSort.cs:157`
(`MaxEncodedChars` is 2048 but no decodable payload exceeds ~1,548 chars, so `>` and `>=` never
differ), `ScaffoldValues.cs:114` (`AdjustToUniversal` does not change whether `TryParse`
succeeds, and `IsDateTime` returns only that), `ScaffoldValues.cs:177` (a `break` whose loop
would reassign the same value), and `ScaffoldAccess.cs:71` (`ConfigureAwait(false)` → `true`).
Stryker has no equivalent-mutant detection; the honest handling is to name them here, which is
section 4's discipline applied to survivors rather than to corpus entries.
