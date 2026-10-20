# nativeweb.assets

**Framework-owned assets left this directory on 2026-08-31 (spec 015 R7).** `boot.js` and the
three design-system stylesheets are now at `libs/nativeweb.web/wwwroot/_nativeweb/`, and
`libs/nativeweb.web/build/NativeWeb.Web.targets` contributes them to every consumer's static web
assets — so there is one copy, the build delivers it, and the fingerprinting and precompression
`AssetUrls` depends on happen to it like any other file under `wwwroot`.

Until then the mechanism was hand-vendoring: canonical here, byte-identical copies under each
app's own `wwwroot/`, and a parity test to catch drift. Two copies that a test proves equal are
still two copies, and a framework whose CSS fix reaches a product only when somebody re-runs a
copy is a framework that cannot ship a CSS fix. The base path is underscore-prefixed
(`/_nativeweb/…`) because the static web assets pipeline treats two assets at one target path as
a build error rather than a precedence question, and an app is free to have its own
`wwwroot/css/base.css`.

**Four shared templates followed on 2026-09-05 (spec 029 R1).** `shared/antiforgery.html`,
`shared/bot-traps.html`, `shared/banner-ok.html` and `shared/banner-error.html` are at
`libs/nativeweb.web/templates/shared/` and the same `.targets` contributes them as
`AdditionalFiles`. Same argument, one library further in: they render framework markup from
framework code, so `bot-traps.html` spelling five `BotTraps` field names as literal text made a
renamed constant a silent failure in every vendored copy.
`tests/nativeweb.repo.tests.unit/FrameworkTemplateTests.cs` refuses a reintroduced copy the way
the parity test used to catch drift, and `NativeWeb.Web.targets` makes an undeclared one a build
error.

Third-party libraries are unchanged: each app still vendors its own htmx, Alpine and fonts, and
`tests/nativeweb.repo.tests.unit/AssetParityTests.cs` still checks those trees agree between
consumers. They are not the framework's to own — what is vendored is an upstream release, and the
only claim is that every consumer is on the same one.

`rollback/` is the one exception, and it is not part of that rule so much as a carve-out from it.
It holds the previous vendored build of a library the repo has just moved off, kept only so a
revert is a file copy rather than a re-download of a version the upstream may have unpublished.
It sits here, outside every `wwwroot`, because spec 012 R1 requires that no superseded build be
reachable, served or published, and R11 requires the build be retained — a directory that no app
copies from satisfies both. Files here are never edited, never served, and are removed when the
version they roll back to stops being a plausible target. Today: `htmx-2.0.10.min.js`, the build
`apps/web` and the skeleton served before the htmx 4.0.0 upgrade.

To change a framework asset: edit the one file under `libs/nativeweb.web/wwwroot/_nativeweb/`.
Nothing to re-copy, and the same for a framework template under
`libs/nativeweb.web/templates/`. To upgrade a vendored third-party library: apply it to every
consumer's `wwwroot/vendor/` in the same branch, which is what the remaining parity test checks.
