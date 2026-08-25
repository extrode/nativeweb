# 010 — Documentation and tutorials

Status: done
Created: 2026-08-25

## Problem

NativeWeb is described in `AGENTS.md` as "both a reusable reference template and the full ground-up
redesign of epass.io", and neither audience can currently get started with it. The repo holds 2,227
lines of reference documentation across eight files in `docs/`, plus two substantial library
READMEs — all of it written for someone already inside the project, none of it answering "I cloned
this, now what."

The front door is worse than absent, it is unfinished. `README.md` still carries the scaffold's own
template placeholders on lines 3, 9, 15 and 16: the one-sentence description of what NativeWeb is,
the command that runs it from a clean clone, the build command and the test command. A reader
reaches the second line of the project and finds an instruction addressed to its author.

Nothing has ever been written for the two journeys that matter. Someone stamping a new product has
`tools/new-product.ps1` documented in the README's lower half, but no path from there to a running
product. Someone maintaining `apps/web` has no route from clone to a green suite across the three
required test tiers. Both discover the eight reference docs only by listing the directory.

This is wanted **before** the epass.io production cutover, because the cutover is the point at
which people other than the author start reading this repo.

## Goals

- A reader with a clean clone and no prior knowledge of this repo reaches a running application and
  a passing test suite by following written steps, without asking anyone.
- A reader who wants their own product, and a reader who wants to work on the IdP, each follow one
  path and are never asked to read the other's steps.
- Every command that appears in the onboarding documentation has been executed successfully before
  it was written down, and the documentation says so.
- The eight existing reference documents become reachable from a single entry point rather than by
  directory listing.
- A reader who finishes the tutorial has produced a working vertical slice of their own, covered at
  all three test tiers the project requires.

## Non-goals

- **Rewriting the eight existing reference documents.** They are accurate and this work links to
  them; correcting their content is separate work.
- **A published documentation website.** The output is Markdown in the repo. (The generated
  `docs/index.html` artifact hub that exists untracked in one worktree is a code-review report
  index and is unrelated to this work.)
- **Filling the placeholder in `skeleton/README.md`.** That file is the *stamped product's* README
  and its placeholder is a prompt addressed to the new product's author, which is correct as it
  stands.
- **API reference material generated from source.** Out of scope at any size.
- **Documenting the epass.io cutover itself**, or the capability gap in
  `docs/architecture/002-epass-cutover-capability-gap.md`. That is a decision awaiting the user, not
  a tutorial.
- **Deployment and operations documentation.** Slice 10's deploy half does not exist yet
  (`work/todo.md`); documenting a procedure that has never been performed would be fiction.

## User stories

- As someone evaluating NativeWeb, I want the first paragraph of the repository to tell me what it
  is and who it is for, so that I can decide in under a minute whether to keep reading.
- As someone who wants to build a product on this architecture, I want a path from clone to my own
  running, tested product, so that I am not reverse-engineering the reference implementation to
  find out how it is meant to be used.
- As someone joining work on the epass.io IdP, I want a path from clone to a green suite across all
  three test tiers, so that my first change is verifiable rather than hopeful.
- As either reader, I want to add one working feature end to end under guidance, so that I learn
  where an endpoint, a template, a query and each tier of test belong before I need to invent that
  placement myself.
- As either reader, I want to know which reference document answers a given question, so that 2,227
  lines of existing prose become an asset rather than a directory I scroll past.
- As a maintainer six months from now, I want to know which documented commands were verified and
  when, so that I can tell a stale instruction from a broken environment.

## Requirements

- **R1** — The repository's front page states in one sentence what NativeWeb is and who it is for,
  and contains no unfilled placeholder text.
- **R2** — The front page gives the commands to build the project and to run its tests, and each
  works from a clean clone on a machine meeting the documented prerequisites.
- **R3** — The prerequisites for working on this repository are stated explicitly, including
  required versions, and each stated version matches what the repository actually requires.
- **R4** — A getting-started path exists that carries a reader from a clean clone to a running
  application, covering database creation, schema application, configuration of any secret the
  application will not start without, and starting the application.
- **R5** — The getting-started path carries the reader on to a green test run, and names each of
  the three required test tiers and what each one covers.
- **R6** — The two audiences are separated explicitly at the point where their paths diverge, and
  the divergence point is stated rather than implied. Neither reader is asked to perform a step
  belonging to the other.
- **R7** — The product-stamping audience is carried from the stamping command to their own running,
  migrated, tested product — not merely to a stamped directory.
- **R8** — A tutorial exists that walks a reader through adding one feature end to end, covering
  the request entry point, the rendered output, the data access, and a test at each of the three
  required tiers.
- **R9** — The tutorial's feature is small enough to complete in one sitting and complete enough to
  exercise every layer named in R8; no layer is described as "left as an exercise".
- **R10** — The tutorial states, for each layer, why the code goes where it goes, and links to the
  reference document that governs that layer rather than restating it.
- **R11** — A single index makes every reference document in `docs/` discoverable, saying for each
  what question it answers.
- **R12** — Every command that appears in any document produced by this work has been executed
  successfully, in the form written, before being written down.
- **R13** — The documentation records what was verified, on what date, and on what operating system
  and toolchain versions, so that a later reader can distinguish a stale instruction from a broken
  local environment.
- **R14** — Documentation produced by this work does not restate content held in the eight existing
  reference documents; where the two overlap, it links.
- **R15** — Nothing produced by this work contradicts `AGENTS.md`, `docs/constitution.md` or
  `docs/conventions.md`. Where a stated rule and the documented practice differ, the difference is
  surfaced rather than smoothed over.

## Acceptance criteria

- **R1** — Given a reader opening the repository root, when they read the first paragraph, then
  they can state what NativeWeb is and who it is for; and a search of the front page for
  placeholder markers returns nothing.
- **R2, R12** — Given a clone of this repository into a directory that has never held it, when each
  documented build and test command is run in the documented order, then each exits successfully.
  This is to be performed as an actual clean clone, not asserted from the working checkout.
- **R3** — Given the stated prerequisite versions, when they are compared against what the
  repository's own build configuration requires, then they agree.
- **R4** — Given a machine with the prerequisites and no database for this project, when the reader
  follows the getting-started steps in order, then the application starts and serves its health
  endpoint. Every step needed is present: a step the author performed but did not write down is a
  failure of this criterion.
- **R5** — Given a completed getting-started run, when the documented test commands are run, then
  all three tiers execute and report no failures, and the reader can say what each tier covers.
- **R6** — Given either audience, when they follow their path, then they are never instructed to
  perform a step belonging to the other; and the point of divergence is stated in the text.
- **R7** — Given the product-stamping path followed to its end, then a stamped product exists, its
  database is migrated, it starts, and its tests pass.
- **R8, R9** — Given a reader who has completed getting-started, when they follow the tutorial to
  the end, then a working feature exists that they built, exercising entry point, rendered output,
  data access and all three test tiers; and no layer is deferred.
- **R10** — Given any layer in the tutorial, then the text says why the code belongs there and
  links to the governing reference document.
- **R11** — Given the index, then every Markdown document under `docs/` intended for a human reader
  is reachable from it, and each entry says what question that document answers.
- **R13** — Given any document produced by this work, then it records the verification date and the
  operating system and toolchain versions used.
- **R14** — Given any passage in the new documentation, then it does not duplicate a passage in the
  eight existing reference documents; overlapping subjects are linked.
- **R15** — Given the new documentation read against `AGENTS.md`, `docs/constitution.md` and
  `docs/conventions.md`, then no instruction contradicts them, and any divergence found is recorded
  rather than hidden.

## Decisions already taken

Recorded here rather than left open, because the user settled them before this spec was written.

- **Scope is the full tutorial set**, not the minimal README fix: front page, getting-started,
  first-feature tutorial and reference index. Chosen 2026-08-25.
- **Both audiences, clearly separated.** Product-stampers and `apps/web` maintainers each get a
  path, diverging after the shared build step. Chosen 2026-08-25.
- **Verification by execution is a hard constraint, not a quality aspiration** (R12). An unrun
  command in a getting-started guide is worse than no guide, because it costs the reader their
  trust in every other step.

## Inferences made rather than asked

- **Output is Markdown in `docs/`**, consistent with every other document in this repository. A
  site generator would be a new toolchain and is a non-goal above.
- **Windows is the primary documented platform**, since it is the development machine and the one
  on which verification can actually be performed. Where a command differs on another platform and
  that difference is known, it is noted; platforms that cannot be verified are not claimed.
- **The tutorial's feature is built in the sample application rather than in `apps/web`**, so that
  a reader following it does not modify the IdP. The exact feature is a `/plan` decision.

## Open questions

None. The two scope decisions were taken before the spec was written and are recorded above; the
remaining choices — which document holds which section, what the tutorial's feature is, and whether
command verification is repeated automatically or dated and manual — are `/plan` decisions rather
than open questions about intent.
