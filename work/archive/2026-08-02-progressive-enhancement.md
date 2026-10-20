# Archived tasklist — spec 004: progressive enhancement layer + formatted HTML output

Shipped to `dev` 2026-08-02. Durable tier: the task breakdown lives at
`docs/specs/004-progressive-enhancement/004-tasks.md` (all ticked — B1 pretty-printer,
B2 manzil formatting fallout (landed empty), B3 boot.js + doc + skeleton, B4 IdP retrofit,
B5 manzil retrofit, close-out). Summary in `CHANGELOG.md` under [Unreleased].

Branches merged `--no-ff` into each repo's `dev`, unpushed: NativeWeb
`feat/render-formatting`, `feat/boot-js`, `feat/nw-retrofit-web`, `fix/pe-e2e-settle-race`,
`docs/spec-004-close-out`; manzil.my `feat/nw-retrofit` (`chore/formatting-fallout` closed
empty, never merged).

End-state verification: NativeWeb 92 render unit + 351 web unit + 236 integration + 54 e2e;
manzil 218 unit + 81 integration + 19 e2e; zero-warning AOT publishes
(`nativeweb.web.exe` 35.2 MB of 42.5 MB budget, `manzil.exe` 29.1 MB); view-source
screenshots of indented output captured against both published binaries.
