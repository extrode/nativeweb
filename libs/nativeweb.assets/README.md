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

To change an asset: edit the canonical file here, re-copy it into every consuming app in the
same branch, and let the parity tests prove the sync.
