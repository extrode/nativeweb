# 019 — Plan
Spec: ./019-spec.md

## Approach

Three small changes and one slow run. A committed `tools/stryker-config.json` carries
`ignore-methods: ["ConfigureAwait"]` and a raised `additional-timeout`; the triage tool passes it
with `--config-file` as an absolute path, since Stryker resolves that option against its working
directory, which is the test project. Each tier's database helper gains a
`SweepLeakedFeedRowsAsync` whose predicate is the cleanup script's predicate plus an age gate on
`created_at`: a `feed-` or `e2e-` id, `created_at > now()`, and
`created_at < UtcNow.AddYears(5).AddMinutes(-30)` computed in C# the same way the tests compute
their dates. The feed test classes take it as an xUnit class fixture, so it runs once per class per
process and before any insert. Then all three targets are re-run with `--record`.

Rejected: a sweep in `SampleProcessFixture` (every e2e class would pay for it, and it is the feed
tests' residue); a stamp inside the id (changes the id format both cleanup scripts key on, for no
gain over `created_at`); a PostgreSQL `interval '5 years'` cutoff (leap-day arithmetic can differ
from `AddYears` by a day, and a day is longer than the failure took to appear).

## Tech / dependencies

Stryker.NET 4.16.0, already installed globally. `ignore-methods` and `additional-timeout` are both
present as option names in its `Stryker.CLI.dll`; config-file only, no CLI flag. xUnit 2.9.3,
which has class fixtures with `IAsyncLifetime` and no assembly fixture. Npgsql, already referenced
by both test projects.

## Changes by area

- `tools/stryker-config.json` — new; the two options. (R1, R2, R6)
- `tools/mutation-triage.ts` — `strykerArgs(target, root)` extracted and exported; `run` uses it;
  header rewritten for the residue as it now is. (R6, R7, R9)
- `tools/mutation-triage.test.ts` — the argument list names the config file and the target. (R7)
- `tests/nativeweb.scaffold.tests.integration/SampleJauntyFactory.cs` —
  `SampleTestDatabase.SweepLeakedFeedRowsAsync()`. (R3, R4)
- `tests/nativeweb.scaffold.tests.integration/LeakedFeedRowsSweep.cs` — new class fixture calling
  it from `InitializeAsync`. (R3)
- `tests/nativeweb.scaffold.tests.integration/PublicFeedTests.cs` —
  `IClassFixture<LeakedFeedRowsSweep>`. (R3)
- `tests/nativeweb.scaffold.tests.integration/LeakedFeedRowsSweepTests.cs` — new; the three-row
  proof. (R5)
- `tests/nativeweb.scaffold.tests.e2e/JauntyBackedAdminTests.cs` — the same method on
  `SampleE2eDatabase`; `tests/nativeweb.scaffold.tests.e2e/LeakedFeedRowsSweep.cs` and the fixture
  on the e2e `PublicFeedTests`. (R3)
- `tools/mutation-baseline.json` — re-recorded; `note` fields unchanged. The configuration that
  produced it is named in the triage header and the CHANGELOG entry, since JSON carries no
  comment. (R8)
- `scripts/cleanup/018-paging-property-tests.ps1` — description paragraph updated. (R9)
- `work/todo.md`, `work/status/current.md`, `CHANGELOG.md`.

## Data / interfaces

```sql
DELETE FROM articles
 WHERE (id LIKE 'feed-%' OR id LIKE 'e2e-%')
   AND created_at > now()
   AND created_at < @cutoff   -- UtcNow.AddYears(5).AddMinutes(-30)
```

The largest offset a feed test adds to its five-year date is one hour, so a row minted now sits at
least thirty minutes past the cutoff, and a row minted two hours ago sits at least thirty minutes
before it whatever its offset.

## Risks & mitigations

- **The sweep deletes a live row.** Only if a test host has been running a feed test for more
  than thirty minutes, which Stryker would already have killed. R5's test pins the three cases.
- **`additional-timeout` changes the score by turning timeouts into survivors.** Expected, and the
  reason R8 re-records the baseline rather than comparing against the old one.
- **The e2e helper diverges from the integration helper.** They already duplicate `InsertAsync`
  and `DeleteByIdPrefixAsync` by design; the sweep follows the same pattern.

## Coverage check

R1, R2, R6 → config file and triage args. R3, R4 → helpers, fixtures. R5 → the sweep test.
R7 → `strykerArgs` and its test. R8 → the `--record` run. R9 → header and script text. None
uncovered.
