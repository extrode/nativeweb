# 2026-09-03 — 009 — Sample content and a public-site visual refresh

Standard tier. Approved by the user on 2026-09-03 through two choices: content arrives as a
**seed migration in the repo**, and the styling is a **token redesign in the sample's own layer**
rather than polish inside the framework palette.

## Why

`nw-dev.example.com` and a local run against `nativeweb_sample_dev` both show an empty
`/articles`, and the reason is an asymmetry rather than a loss: `Seed.Fill` in
`apps/sample/Program.cs:259` gives the memory path three articles, and the database path has
never had one — migration 0004's backfill reported `UPDATE 0` because `articles` was already
empty. Whichever store a run picks should show the same site, so the fix is one dataset written
twice, not a hand-typed row.

The public pages also do not yet show what spec 023 added. The list and the article page print
`CreatedAt` while `PublishedArticle.PublishedOn()` exists precisely to answer "the date the site
dates this by", and no public page names a category at all.

## Approach

**Content.** Grow the shared demo dataset from three articles to eight, in both stores, and keep
`a1`, `a2` and `a3` byte-identical — every existing assertion about `lean-native-web`,
`binding-forms-under-nativeaot` and the `scaffolding-without-reflection` draft keeps its meaning,
and the 022 R4 invariants hold (one article with two tags, one with none, one tag whose only
article is unpublished). The five new rows carry the cases the site cannot otherwise show: an
Archived row, a second author, an article with no category, a long body that proves the reading
column, and a publication date that predates its creation time.

`db/sample/migrations/0005_seed_demo_content.sql` inserts all eight with
`ON CONFLICT (id) DO NOTHING`, so it is safe on a database an admin has already used and safe to
re-run. It runs on the next nw-dev deploy like any other migration.

**Styling.** `tokens.css`, `base.css` and `components.css` are the framework's, vendored
byte-identical and guarded by `AssetParityTests` — untouched. The redesign is token overrides at
the top of `apps/sample/wwwroot/css/sample.css`, so every framework component inherits it without
being edited. Both themes get values, and every foreground/background pair is measured against
WCAG 2.1 AA (4.5:1 body, 3:1 large) with `tools/contrast-check.ts` rather than eyeballed — the
existing palette carries two documented divergences won in an a11y pass, and losing them silently
would be the easiest mistake here.

## Changes by area

- `apps/sample/templates/` — the list and the article page date by
  `PublishedArticle.PublishedOn()` rather than `CreatedAt`. That method exists to answer exactly
  this question and no page called it, which is what made a8 (backdated) render in the wrong
  place while the SQL order put it in the right one.
- `apps/sample/Program.cs` — `Seed.Fill` grows to eight articles and links their tags.
- `db/sample/migrations/0005_seed_demo_content.sql` — the same eight rows and links.
- `apps/sample/wwwroot/css/sample.css` — token overrides for both themes, then a rewritten app
  layer: hero, article cards, reading column, tag pills that carry the tag's own colour, footer.
- `apps/sample/templates/` — `layout/page.html`, `site/home.html`, `articles/index.html`,
  `articles/results.html`, `articles/show.html`, `tags/index.html`, `tags/show.html`.
- `apps/sample/locales/en.json` — the new chrome and empty-state strings.
- `tools/contrast-check.ts` — a Deno script that parses the overrides and asserts the ratios.

## Risks

- **Eight rows land in `nativeweb_sample_test`**, which every SQL-backed suite shares. The suites
  are written to tolerate other rows (per-test markers, relative ordering — the 019 lesson), but
  that is a claim to verify by running all three tiers, not to assume.
- **Naming the category on a public page is deliberately out of scope.** `PublishedArticle` has
  no category, and giving it one touches both readers, three query files, the JSON contract, the
  CSV columns and every test that constructs the record — churn out of proportion to one label.
  Noted in `work/todo.md` instead. No public contract changes in this plan.
- **Contrast regression** is the way a redesign fails silently. Measured, not judged.

## Verification

Scaffold unit, sample unit, scaffold integration and e2e all green; the contrast script exits 0;
`deno run -A ~/.claude/tools/structure-audit.js` clean; the site read in a browser at both themes.
Full two-lens `/code-review` before merging `--no-ff` into `dev`.
