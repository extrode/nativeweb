# nativeweb.assets

This folder holds one thing: `rollback/htmx-2.0.10.min.js`, the previous htmx build, kept so a
revert to it is a file copy rather than a re-download. It sits outside every `wwwroot`, so it is
never served or published. Files here are never edited, and are removed when the version they roll
back to stops being a plausible target. It is the htmx build `apps/web` and the skeleton served
before the htmx 4.0.0 upgrade.

## Where framework assets live

- `boot.js` and the three design-system stylesheets are in
  `libs/nativeweb.web/wwwroot/_nativeweb/`.
- The shared templates (`antiforgery.html`, `bot-traps.html`, `banner-ok.html`,
  `banner-error.html`) are in `libs/nativeweb.web/templates/shared/`.
- `libs/nativeweb.web/build/NativeWeb.Web.targets` delivers both to every consumer: the assets as
  static web assets, the templates as `AdditionalFiles`. There is one copy of each. Fingerprinting
  and precompression, which `AssetUrls` depends on, apply to them like any file under `wwwroot`.
- The base path is `/_nativeweb/…`. The underscore keeps it clear of an app's own
  `wwwroot/css/base.css`: the static web assets pipeline fails the build when two assets share a
  target path.
- `tests/nativeweb.repo.tests.unit/FrameworkTemplateTests.cs` refuses a copy of a shared template in
  an app, and `NativeWeb.Web.targets` makes an undeclared one a build error.

## Third-party libraries

Each app vendors its own htmx, Alpine.js and fonts under `wwwroot/vendor/`, each with its license
file: `apps/sample/wwwroot/vendor/` and `skeleton/apps/__product__/wwwroot/vendor/`. These are
upstream releases, not the framework's to own. `tests/nativeweb.repo.tests.unit/AssetParityTests.cs`
checks that every consumer is on the same release.

## Changing assets

- Framework asset: edit the one file under `libs/nativeweb.web/wwwroot/_nativeweb/`. The same goes
  for a template under `libs/nativeweb.web/templates/`. Nothing to re-copy.
- Vendored library: upgrade it in every consumer's `wwwroot/vendor/` in the same branch.
