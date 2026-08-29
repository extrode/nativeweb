# nativeweb.assets

Canonical client-side assets shared by every Lean Native Web app. Not a .NET project: nothing
here compiles — apps vendor byte-identical copies under their own
`wwwroot/vendor/nativeweb/` (the same pattern as the vendored htmx/Alpine), and a unit test in
each app asserts the copy matches the canonical file, so drift fails the suite instead of
shipping.

What belongs here: framework-owned JS/CSS that every app must serve identically (`boot.js`, the
`data-nw-*` enhancement layer — see `docs/01-architecture/progressive-enhancement.md`). What does not:
third-party vendored libraries (each app vendors its own htmx/Alpine/fonts), app-specific
assets, and anything generated.

`rollback/` is the one exception, and it is not part of that rule so much as a carve-out from it.
It holds the previous vendored build of a library the repo has just moved off, kept only so a
revert is a file copy rather than a re-download of a version the upstream may have unpublished.
It sits here, outside every `wwwroot`, because spec 012 R1 requires that no superseded build be
reachable, served or published, and R11 requires the build be retained — a directory that no app
copies from satisfies both. Files here are never edited, never served, and are removed when the
version they roll back to stops being a plausible target. Today: `htmx-2.0.10.min.js`, the build
`apps/web` and the skeleton served before the htmx 4.0.0 upgrade.

To change an asset: edit the canonical file here, re-copy it into every consuming app in the
same branch, and let the parity tests prove the sync.
