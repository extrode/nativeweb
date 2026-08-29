# Contributing to NativeWeb

> [!IMPORTANT]
> **Contributions are not being accepted yet.** The agreement below is published in draft by its
> drafters and says of itself: *"Do not adopt it, and do not accept Contributions under it, until
> it is marked final."* NativeWeb's adoption is therefore recorded here in advance and is **not in
> force**. Until ISL-CLA is marked final, a pull request cannot be merged, whoever sends it.
> **Issues, bug reports and written design proposals are welcome now** — see
> [What to send instead](#what-to-send-instead), which is the preferred route regardless.

## The agreement

This project adopts the **Islamic Software License Contributor License Agreement (ISL-CLA),
Version 1.1**, by reference. The text is at <https://islamiclicense.org/isl-cla/1.1/CLA.md> and is
not reproduced or edited here — the agreement is adopted whole or not at all, and deleting a clause
would produce a modified ISL-CLA, which its reproduction notice forbids.

**Why 1.1 and not 1.0.** Adoption is by name *and version*, so the version has to name the text
whose requirements are met below. ISL-CLA 1.0 was twice revised in place — a §9.2 forum clause and
a fifth adoption item, then a rewritten item (d) — and on 2026-08-29 its drafters undid that:
`ISL-CLA-1.0.md` was frozen back to its original **four**-item text with a single §9.2 placeholder,
and the revisions became 1.1. The five statements below, and the forum in particular, exist only in
1.1.

Its *How a Project adopts this Agreement* section requires a project to state five things. They
are stated here:

| required statement | NativeWeb |
|---|---|
| **(a)** which ISL variant the project is licensed under | **ISL-NETC 1.0** — see [`LICENSE.md`](./LICENSE.md). Not ISL-R, so **§2 (Limited Consent to Modify) does not apply**: ISL-NETC permits modification already, and §2 is inert here. |
| **(b)** whether the project adopts optional **§6 (No AI-Generated Contributions)** | **Adopted.** §6 applies in full to every contribution to this project. |
| **(c)** the jurisdiction whose law governs the agreement, completing the first **§9.2** placeholder | **Commonwealth of Virginia**, matching the jurisdiction Extrode LLC's other licenses use. |
| **(d)** who the Licensor is | **Extrode LLC**. This project is on an ISL-NETC **1.0** text, which names Ikhbat Foundation in its §1.2 because the family was drafted as the Foundation's own; under the second limb of (d), the Licensor is the party that applied the text. The override is recorded in [`NOTICE.md`](./NOTICE.md). From ISL-NETC 1.1 that limb is unnecessary — §1.2 becomes a `[LICENSOR]` placeholder the adopter fills — and this row changes when the re-sync happens. |
| **(e)** the forum in which disputes are brought, completing the second **§9.2** placeholder | **The courts located in the Commonwealth of Virginia**, matching the forum clause in Extrode LLC's other licenses. |

Absent (b), §6 would not apply and the AI prohibition below would bind nobody. A project that
omits (c) has left its governing law unsettled, and one that omits (e) has left the court
unsettled — the more expensive of the two to leave open. Either is a defect in the adoption rather
than a permission to proceed without one.

## No AI-generated contributions

**Contributions must be written by a human being.** ISL-CLA §6.1: contributions generated in
whole or in part by an artificial intelligence or machine-learning system are not accepted. That
covers large language models, AI coding assistants, completion systems that emit more than a
trivial single-token or single-identifier suggestion, and any automated generator trained on
third-party source.

**Not accepted** (§6.3): patches written by an AI assistant · AI-generated tests or documentation
· AI-translated or AI-refactored code · code you cannot explain line by line.

By opening a pull request you represent, under §6.2, that the work was authored by a human. Under
§6.5 the maintainers may ask you to confirm authorship, and may decline a contribution they
believe to be AI-generated without explaining how they reached that view.

### The asymmetry, stated plainly

**This repository is itself developed with AI assistance, under the Licensor's own review, and
that is not a double standard being hidden.** ISL-CLA §6.4(3) is the reason, and it is worth
reading rather than paraphrasing:

> A Licensor's own use of such tooling is a decision about its own liability. Where a Licensor
> uses AI tooling on its own code, under its own review and responsibility, that is a choice it
> makes for itself. It is not a choice it can make on Your behalf, or on behalf of the code You
> send it.

The other two reasons in §6.4: **provenance** — a contribution whose copyright status cannot be
established is one for which the grants in §§3 and 4 cannot honestly be warranted — and
**accountability**, that every line should have a human who understood it when it was written and
can answer for it afterwards. Extrode carries that risk for its own code. It will not accept it
from a stranger's patch.

## What to send instead

The route this project prefers, and the one that works today while the agreement is still draft:

- a **bug report in your own words**;
- **reproduction steps** and the input that fails;
- a **prose description** of a proposed design or change;
- a **written explanation of a defect** and where in the codebase it lies.

**Describe the problem or the change in writing, and the maintainers will implement it.** A
precise, well-argued issue is more useful here than a patch.

## How to sign, once the agreement is final

ISL-CLA §9.6: acceptance is a signed entry in `CONTRIBUTORS.md`, added in the first pull request
in which you submit a contribution — or in a pull request that adds only the entry, if you want
certainty before touching the code. The commit that adds your row, made from your own account, is
the record of your agreement.

```markdown
| Name | Account | Date | Agreement |
|---|---|---|---|
| Amina Yusuf | @aminay | 2026-08-29 | ISL-CLA-1.1 |
```

Acceptance runs from the first act of preparation, not from the merge. If you never add the
entry, acceptance fails.

## What you are granting, and what you keep

Read §§3, 4 and 5 of the agreement rather than this summary, but in short:

- **§3.1** grants Extrode a perpetual, worldwide, non-exclusive, royalty-free, irrevocable license
  to use, modify, sublicense and distribute your contribution. **§3.2** spells out that this
  includes paid commercial releases under a different ISL variant, without further permission from
  you and without payment to you.
- **§3.3** — you keep full ownership. You may use, license and publish your work elsewhere however
  you wish.
- **§4** grants a patent license limited to claims your contribution necessarily infringes, and
  terminates for any entity that brings patent litigation over the project.
- **§5** — you represent the work is your own, that you have the right to grant it (including from
  an employer where one has rights in your work), that it is unencumbered, and that any part which
  is not yours is identified with its source and license at the time you submit it.
- **§7** — the Ethical Use Restrictions in ISL-NETC §§4 and 5 are untouched by any of this. They
  bind you as a contributor as they bind everyone else.
- **§8** — the maintainers are under no obligation to review, accept, merge or keep anything.

## Working in this repository

If a written proposal is accepted and you are asked to implement it, the repository's own rules
apply: [`AGENTS.md`](./AGENTS.md) is binding — `main` is release-only, `dev` is the integration
branch, work happens on a `feat/`/`fix/`/`chore/`/`refactor/`/`docs/` branch off `dev` and merges
back with `--no-ff`, and every feature ships with unit, integration and end-to-end tests in the
same branch.
