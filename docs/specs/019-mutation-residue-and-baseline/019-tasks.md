# 019 — Tasks
Plan: ./019-plan.md

- [x] T1 Stryker config and triage args — files: `tools/stryker-config.json`, `tools/mutation-triage.ts`, `tools/mutation-triage.test.ts` — covers: R1 R2 R6 R7 — done when: `deno test -q tools/` passes with a test that the argument list names the config file, and that test fails when the flag is dropped
- [x] T2 Integration-tier sweep — files: `SampleJauntyFactory.cs`, `LeakedFeedRowsSweep.cs`, `PublicFeedTests.cs`, `LeakedFeedRowsSweepTests.cs` under `tests/nativeweb.scaffold.tests.integration/` — covers: R3 R4 R5 — done when: the three-row test passes, and fails when the age gate is removed
- [x] T3 E2e-tier sweep — files: `JauntyBackedAdminTests.cs`, `LeakedFeedRowsSweep.cs`, `PublicFeedTests.cs` under `tests/nativeweb.scaffold.tests.e2e/` — covers: R3 — done when: the e2e project builds and its feed tests pass
- [x] T4 Re-record the baseline — files: `tools/mutation-baseline.json` — covers: R1 R2 R8 — done when: all three targets ran with `--record` and the `sample-integration` report has no mutant inside a `ConfigureAwait` call
- [x] T5 Records — files: `tools/mutation-triage.ts` header, `scripts/cleanup/018-paging-property-tests.ps1`, `work/todo.md`, `work/status/current.md`, `CHANGELOG.md`, `019-spec.md` status — covers: R9 — done when: acceptance re-read and `Status: done`
