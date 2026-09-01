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

**Left open, recorded rather than chased.** Four survivors are equivalent mutants — a change
with no observable difference, so no test can kill them: `ScaffoldSort.cs:157`
(`MaxEncodedChars` is 2048 but no decodable payload exceeds ~1,548 chars, so `>` and `>=` never
differ), `ScaffoldValues.cs:114` (`AdjustToUniversal` does not change whether `TryParse`
succeeds, and `IsDateTime` returns only that), `ScaffoldValues.cs:177` (a `break` whose loop
would reassign the same value), and `ScaffoldAccess.cs:71` (`ConfigureAwait(false)` → `true`).
Stryker has no equivalent-mutant detection; the honest handling is to name them here, which is
section 4's discipline applied to survivors rather than to corpus entries.
