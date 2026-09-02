# 019 — Mutation runs that leave nothing behind, and a baseline that means what it says
Status: draft
Created: 2026-09-02

## Problem

`tools/mutation-triage.ts` compares three Stryker scores against `tools/mutation-baseline.json`,
and two things recorded on 2026-09-01 make the comparison worse than it looks. First, the raw
survivor counts are inflated by mutants no test can kill: roughly fifteen of
`JauntyArticleStore`'s thirty-four integration-tier survivors are `.ConfigureAwait(false)` flipped
to `true`, which is behaviour-identical under a test host, so the score reads as a gap that is not
one. Second, the `sample-integration` target leaves live rows in the shared test database. Stryker
kills a test host it judges hung, a killed process never reaches the `finally` that removes its
fixture rows, and the feed tests date their rows five years into the future on purpose. One run
left five such rows and an e2e feed test failed on them an hour later. Today the residue is handed
to the operator through `scripts/cleanup/018-paging-property-tests.ps1` after every run.

## Goals

- A mutation score in this repository counts only mutants a test could kill.
- A feed test run, in either tier, finds the database as it needs it whatever the previous run
  left behind, with no operator step in between.
- Fewer test hosts are killed under load, so fewer rows leak in the first place and the
  `sample-integration` figure drifts less.
- The baseline is re-recorded from the new configuration, so the next comparison is against a
  figure produced the same way.

## Non-goals

- Closing any of the real survivors the 2026-09-01 triage named (`ScaffoldAccess.CanReadAsync`,
  `DemoSignIn.cs`, `Models.cs`). This work changes what is counted and what is left behind, not
  the tests that do the killing.
- Equivalent-mutant detection. Stryker has none; the four equivalent mutants named in
  `work/todo.md` stay named there.
- A shared TRUNCATE, or any test that assumes an empty table. Both suites refuse that on purpose
  and the sweep keeps the discipline: only rows the feed suites minted, only when they can no
  longer belong to a live test.
- Running Stryker in CI.

## User stories

- As a maintainer reading the triage output, I want the survivor count to be a list of things a
  test could have caught, so that I do not triage fifteen `ConfigureAwait` flips to find the one
  real gap.
- As a maintainer running the suites after a mutation run, I want the feed tests to pass without
  first running a cleanup script, so that a leaked row is not a failure I diagnose twice.
- As the operator, I want the cleanup script to be a rarely-needed fallback rather than a step
  after every run.

## Requirements

- **R1** Mutations inside `ConfigureAwait(...)` arguments are excluded from every target's
  mutant set and from the score's denominator.
- **R2** Each mutant's test run is allowed more time beyond the initial run's duration than
  Stryker's default, so a host that is slow under load is not killed for it.
- **R3** Before a feed test in the integration tier or the e2e tier inserts its rows, rows a
  previous feed run left behind are removed. A row counts as left behind only when it carries a
  feed-suite id prefix, is dated in the future, and was minted long enough ago that no test could
  still be using it.
- **R4** The sweep never removes a row a concurrently running test could be using. Two overlapping
  runs, which Stryker produces by design, do not collide through it.
- **R5** The sweep is proved by a test that plants a leaked row, a fresh row and a present-dated
  row under the same prefix, runs the sweep, and finds only the leaked one gone.
- **R6** The configuration Stryker runs under is one committed file that the triage tool passes,
  so a hand-run `dotnet stryker` from the tool's working directory with that file behaves the
  same as the tool.
- **R7** The triage tool's argument construction is testable without invoking Stryker.
- **R8** `tools/mutation-baseline.json` is re-recorded from a full run of all three targets under
  the new configuration, in the same branch.
- **R9** `scripts/cleanup/018-paging-property-tests.ps1` and the triage tool's header describe the
  residue as it now is: normally gone by the next run, with the script as the fallback.

## Acceptance criteria

- **R1** — Given a source file with `.ConfigureAwait(false)`, When a target is run, Then the
  report contains no mutant at that call and the score's denominator excludes it.
- **R3/R4/R5** — Given a `feed-` row dated five years out and minted two hours ago, a `feed-` row
  dated five years out and minted now, and a `feed-` row dated in the past, When the sweep runs,
  Then the first is gone and the other two remain.
- **R6/R7** — Given the triage tool's argument list for a target, Then it names the committed
  configuration file, and a test asserts that without running Stryker.
- **R8** — Given the branch is merged, Then the baseline's `recorded` date is this work's and its
  three figures come from one run each under the committed configuration.

## Open questions

None. Two decisions were taken while grounding and are recorded so the plan does not reopen them:

- **Both fixes, not one.** The `work/todo.md` entry offered a higher Stryker timeout *or* a
  prefix sweep. The timeout reduces leaks; only the sweep removes them; neither alone closes the
  operator step.
- **Age, not liveness.** A sweep by prefix alone would delete a concurrent host's live rows, since
  Stryker runs test hosts in parallel against the one database. Rows are dated
  `UtcNow.AddYears(5)` plus at most one hour, so the mint time is recoverable from `created_at`,
  and a row minted more than thirty minutes before the sweep cannot belong to a test that is
  still running.
