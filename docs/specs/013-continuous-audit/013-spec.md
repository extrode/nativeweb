# 013 — Continuous read-only audit
Status: draft
Created: 2026-08-29

## Problem

Defects in this repository are found in two ways today, and both are bursts. `/code-review` runs
against one branch diff at the moment it is about to merge, so it sees the change and not the
code the change landed on top of. A spec's acceptance review runs once, when the spec closes.
Between those bursts nothing looks at the repository at all.

That leaves two blind spots. The first is **age**: code that merged clean six months ago has
never been re-read, and the assumptions it was written against have moved since — spec 012 found
htmx 4 changed back-navigation semantics under code nobody had reason to reopen. The second is
**breadth**: a reviewer given one diff cannot be asked "what else in this repo has this same
shape", so a defect class is fixed in the one place it was noticed and left standing in the four
places it was not.

Both blind spots want the same thing: something that reads a slice of the whole repository on a
schedule, remembers what it already said, and hands a person a short list instead of a long one.
What does not want to be automated is the deciding. A finding is worth acting on or it is noise,
and that judgement — and any change to the repository that follows from it — stays with the user.

## Goals

- A defect report exists for parts of the repository that no branch diff has caused anyone to
  read, and it grows to cover the repository over weeks rather than in one pass.
- Triage takes minutes, not an afternoon: the digest a session opens contains findings that are
  new, verified, and not previously rejected.
- The audit cannot damage anything. Not "is careful not to" — cannot.
- The cost of running it every day is known, bounded, and small enough that nobody has to think
  about it.
- A run that fails, or does not happen, is visible at the next triage rather than silently
  absent.

## Non-goals

- **Fixing anything.** The audit reports. Every repository change that results is made by an
  interactive session with the user's agreement, on a branch, through the normal flow.
- **Replacing `/code-review`.** The pre-merge review looks at a diff with full context and a
  shell. This looks at standing code on a schedule. They find different things and both stay.
- **Auditing other repositories.** 013 covers `nativeweb` only. The other 65 repositories under
  the source root are out of scope until the loop is proven on one.
- **Running the test suite.** Executing tests is a separate concern with its own cost and its own
  failure modes, and CI already owns it. The audit reads.
- **A dashboard, a web UI, or a notification service.** The digest is read by a session on
  request. Nothing is pushed anywhere.
- **Deciding severity for the user.** The audit ranks by evidence, not by importance. What
  matters is a judgement about this project that the audit does not have.

## User stories

- As the maintainer, I want the parts of the codebase nobody has opened in months to have been
  read by something, so that a defect's age is not the reason it survives.
- As the maintainer, I want to sit down once a day to a list of new findings rather than a
  re-run of yesterday's list, so that triage stays a habit instead of becoming a chore I skip.
- As the maintainer, I want a finding I have already rejected to stay rejected, so that
  disagreeing with the audit costs me one decision rather than one per day forever.
- As the maintainer, I want to approve any change to the repository myself, so that an unattended
  process can never be the reason something broke.
- As an interactive session, I want each finding to carry its file, its line, and how to confirm
  it, so that I can verify or refute it without re-deriving the whole thing.
- As the maintainer, I want to know what the audit has and has not covered, so that "no findings"
  means something.

## Requirements

- **R1** The audit cannot modify the repository under audit. This is a property of how it is
  arranged, not a rule it is asked to follow: it must be structurally incapable of writing to
  the working tree, committing, pushing, or altering any branch or remote.
- **R2** The audit runs unattended on a host that is not the workstation the maintainer works on,
  on a daily schedule, and resumes that schedule after the host reboots.
- **R3** Each run examines a bounded slice of the repository, not all of it. The bound is
  configured, and exceeding it ends the run rather than continuing.
- **R4** Coverage is recorded. At any time it can be stated which areas have been audited, when
  each was last audited, and which have never been audited at all.
- **R5** Slice selection prefers what has been least recently audited, so coverage accumulates
  and the same area is not re-read while others have never been read.
- **R6** Every finding records: the file and line it concerns, what is wrong, why it matters, and
  how a reader can confirm or refute it. A finding missing any of these does not enter the
  ledger.
- **R7** A finding already in the ledger is not re-reported. Identity survives a line number
  shifting, so that an unrelated edit above a defect does not make it a new defect.
- **R8** Every finding carries a state — `new`, `accepted`, `rejected`, or `fixed` — and every
  transition records the date and the reason. A `rejected` finding is never resurfaced.
- **R9** A single command produces a digest for an interactive session: findings that are new
  since the last triage, ranked, with the coverage and health facts of R4 and R12 alongside.
- **R10** A finding reaching the digest has been checked by something other than whatever
  produced it. A finding that could not be checked is marked unverified and ranks below every
  verified finding.
- **R11** Each run records what it cost and how long it took, and the digest reports the trend.
- **R12** A run that fails, or that does not start, is detectable from the digest. Absence is
  reported as absence, never as "no findings".
- **R13** No credential, key, token, connection string or customer data appears in a report or a
  ledger entry. Where a finding is *about* such a value, it names the location and does not quote
  the value.
- **R14** Acting on a finding requires the user's agreement. Nothing in the audit path opens a
  branch, writes a file in the repository, or changes a finding's state to `accepted` without it.
- **R15** The audit host holds no secret it does not need. It must not hold production
  credentials, deployment keys, or any database password for a live system.

## Acceptance criteria

- **R1** — Given the audit runs to completion, when the repository's working tree is inspected on
  the workstation and on the audit host, then `git status` is clean in both and no branch, tag or
  remote has changed. Given the audit process is inspected, when its access to the source of
  truth is traced, then it holds no writable path to it.
- **R2** — Given the audit host is rebooted, when a day passes, then a run has happened without
  anyone starting it.
- **R3** — Given a slice larger than the configured bound, when the run reaches the bound, then
  it stops and records that it stopped at the bound, and the digest says so.
- **R4** — Given any point in time, when coverage is requested, then every area of the repository
  is listed with its last-audited date or the word `never`.
- **R5** — Given area A was audited yesterday and area B has never been audited, when the next
  slice is selected, then it is B.
- **R6** — Given a produced finding lacking a line reference or a confirmation step, when it is
  offered to the ledger, then it is rejected and the rejection is counted in the run's record.
- **R7** — Given a finding recorded on day one, when the file is edited above it on day two and
  the audit re-reads that area on day three, then the digest shows no new finding for it.
- **R8** — Given a finding marked `rejected` with a reason, when every subsequent run and digest
  is examined, then it never appears again.
- **R9** — Given several days of runs and no triage, when the digest is produced, then it shows
  every new finding since the last triage exactly once, and running it twice without triaging
  shows the same list.
- **R10** — Given a finding that a check refuted, when the digest is produced, then that finding
  is absent and the refutation count is stated. Given a finding that could not be checked, then
  it appears marked unverified, below the verified ones.
- **R11** — Given a run, when its record is read, then it states duration and cost. Given a
  digest, then it states the trend across recent runs.
- **R12** — Given the schedule fires and the run crashes, when the digest is produced, then it
  reports the failure and its error. Given the schedule does not fire at all, when the digest is
  produced, then it reports the gap by date.
- **R13** — Given the full report and ledger corpus, when it is searched for the patterns that
  match keys, tokens and connection strings, then there are no matches. Given a finding about a
  hard-coded secret, then it names file and line and does not reproduce the value.
- **R14** — Given a finding a session judges worth acting on, when the session proceeds, then it
  asks the user before any repository change, and the finding's state does not become `accepted`
  until the user says so.
- **R15** — Given the audit host, when its stored configuration and environment are inspected,
  then no production credential, deployment key or live database password is present.

## Open questions

None. The four decisions this needed — host, scope, cost pool, triage authority — were taken by
the user on 2026-08-29 and are recorded in `013-plan.md`.
