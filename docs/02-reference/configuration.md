# Configuration reference

Every setting the framework reads, what it defaults to, and what a wrong or absent value costs.

**This page is enforced.** `tests/nativeweb.repo.tests.unit/DocsClaimsTests.cs` extracts the key
names from the two binders below at run time and fails when this file names a key the code does
not read, or omits one it does. An earlier version of this page was not enforced, described an
application that had left the repository, and stayed wrong for months while being served at
`/docs`; the guard is the lesson.

## The section prefix

NativeWeb reads nothing from a fixed key path. Every setting lives under a section the product
chooses, passed to `NativeWebOptions.FromConfiguration(builder.Configuration, "<Name>")`. The
reference application uses `Sample` (`apps/sample/Program.cs`); a product stamped from
`skeleton/` uses its own name. Keys below are written section-relative — read `{Section}` as
whatever your composition root passed.

| Source | When | Notes |
|---|---|---|
| `appsettings.json` | always | Committed. Never a secret |
| User secrets | Development only | `CreateSlimBuilder` does not load them; add them explicitly under `IsDevelopment()` |
| Environment variables | always | The deployment path |

**Environment-variable spelling replaces `:` with `__`.** `Sample:Db:MaxAutoPrepare` is
`Sample__Db__MaxAutoPrepare`.

## Framework settings

Bound by `NativeWebOptions.TryFromConfiguration` in `libs/nativeweb.web/NativeWebOptions.cs`.
The **On a bad value** column is the one to read: three of these stop the process at startup
with the key named on stderr, and the rest fall back silently.

| Key | Default | On a bad value |
|---|---|---|
| `{Section}:Db:ConnectionString` | none | Null. The app answers `/healthz` but reports not-ready |
| `{Section}:Db:CommandTimeoutSeconds` | driver default | Ignored unless a positive integer |
| `{Section}:Db:MaxAutoPrepare` | `20` | Falls back to 20 |
| `{Section}:ForwardedHeaders:KnownProxies` | empty | Unparseable entries are **skipped, not fatal** — a typo costs a degraded rate-limit partition, not an outage. **Required behind a proxy**: `deploy.sh` refuses to promote a release without it, because unset means ASP.NET Core trusts loopback only, `RemoteIpAddress` is the proxy hop, and every client shares one rate-limit partition with nothing logged |
| `{Section}:Telemetry:Enabled` | `false` | **Fails startup.** `"1"`, `"yes"` and `"Enabled"` are all things an operator writes meaning on, and every one of them would read as off under an equality check |
| `{Section}:DataProtection:KeyDirectory` | a directory under the process's home | A **configured** value is taken as given and never probed — an operator's explicit statement, checked instead by `deploy.sh`'s preflight against `ReadWritePaths`. Only the fallback is proved, and **fails startup** when there is no home to fall back to |
| `{Section}:BotTraps:MinAgeSeconds` | unset, and the trap still runs: `BotTrapOptions.MinAge` is 3 s and the binder overrides it only when the key is set | **Fails startup** unless a non-negative number |
| `{Section}:RateLimit:General:PermitLimit` | `120` | Falls back to 120; `0` and negatives take the default rather than opening or closing the policy |
| `{Section}:RateLimit:CspReport:PermitLimit` | `10` | Falls back to 10 |
| `{Section}:Readiness:CacheSeconds` | `1` | Clamped to a 30-second maximum |
| `{Section}:SecurityContact` | unset, serves no `/security.txt` | **Fails startup** unless a bare email address or a `mailto:`, `https:` or `tel:` URI on one line |

### Outside the section

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:{Section}` | none | What `apps/sample` reads, in preference to `{Section}:Db:ConnectionString`. Fill the one your binary uses |
| `OTEL_SERVICE_NAME` | the application name | At the configuration root, not under the section: it is the standard environment variable |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | none | Read by the OpenTelemetry exporter itself. `deploy.sh` refuses to promote when telemetry is on and this is unset |

### Three that stop the process

`Telemetry:Enabled`, `DataProtection:KeyDirectory` and `SecurityContact` fail startup rather
than defaulting, and each for the same reason: the silent version of the failure is invisible.
Telemetry an operator believes is exporting and is not; a key ring that cannot persist, so every
restart remints the antiforgery keys and every form POST answers 400 after a deploy while
`/readyz` stays green; a `/security.txt` nobody can act on. `KeyDirectory` is the partial case:
what stops the process is having no usable fallback, not a configured directory that turns out to
be unwritable, which the deploy preflight catches rather than startup. The exit is code 78 with the key
named on stderr — never a modal dialog, which under NativeAOT on Windows a supervisor reads as
a hung service.

### Not read, despite appearances

`{Section}:ForwardedHeaders:ForwardLimit` is **seeded by `scripts/deploy/provision.sh` and bound
by nothing.** `AddNativeWebForwardedHeaders` sets `ForwardedHeaders` and `KnownProxies` and
leaves `ForwardLimit` at the ASP.NET Core default of 1. On a two-hop deployment (`provision.sh
--edge`) that means only the local hop is unwound and every request appears to originate from
the edge proxy — the exact failure the seeded comment says it prevents. Recorded in
`work/todo.md`; do not rely on setting it.

## Sign-in settings

Bound by `SignInConfiguration` in `libs/nativeweb.signin/SignInConfiguration.cs`, under a
section the product names when it calls `AddSignIn`. See
`libs/nativeweb.signin/README.md` for what the library does with them.

| Key | Default |
|---|---|
| `AuthorizationEndpoint`, `TokenEndpoint`, `UserInfoEndpoint`, `IntrospectionEndpoint` | empty |
| `ClientId`, `ClientSecret`, `RedirectUri` | empty |
| `ClientAuthentication` | `ClientSecretBasic` |
| `Scopes` | the library's default set, **replaced wholesale** when configured, never merged |
| `CookieName` | `__Host-signin` |
| `LoginPath` | `/signin` |
| `CallbackPath` | `/signin/callback` |
| `LogoutPath` | `/signout` |
| `SignedOutPath`, `FailurePath`, `DefaultReturnPath` | `/` |
| `FlowLifetimeSeconds` | 15 minutes |
| `SessionLifetimeSeconds` | 14 days |
| `RefreshSkewSeconds` | 2 minutes |
| `IntrospectionCacheLifetimeSeconds` | 30 seconds |
| `RetainTokens` | `true` |
| `StoreEncryptionKey` | none. Base64, because that is what a secret manager hands back and the only encoding that survives an environment variable without a length surprise. It must decode to exactly `StoreDataProtector.KeyBytes`, and a single repeated byte is refused outright — `new byte[32]` is what a placeholder and a copied example both look like, and it boots a host whose stored provider tokens anyone can decrypt. Generate one with `RandomNumberGenerator.GetBytes(32)` |
| `AllowEphemeralStoreEncryptionKey` | `false` |

## What a provisioned host holds

`scripts/deploy/provision.sh` seeds five files under `/etc/nativeweb/`. It writes them empty
where a value is a secret: a script that generates a production credential is a script that has
seen it.

| File | Mode | Holds |
|---|---|---|
| `app.env` | `0644 root:<group>` | `ASPNETCORE_ENVIRONMENT`, the forwarded-headers stanza, `DataProtection:KeyDirectory`, `Telemetry:Enabled` |
| `secrets.env` | `0640 root:<group>` | The connection string, in both spellings — fill the one your binary reads and delete the other |
| `blue.env`, `green.env` | `0644 root:<group>` | `ASPNETCORE_URLS` for each colour, ports 5011 and 5012 |
| `backup.env` | `0640 root:root` | Read by `scripts/deploy/backup.sh` and `scripts/deploy/restore.sh`, not by the application |

`DataProtection:KeyDirectory` must stay inside `/var/lib/nativeweb`: `ReadWritePaths` in the
systemd unit names that directory and `ProtectSystem=strict` makes the rest read-only.

## Related

- `docs/02-reference/deployment.md` — the runbook these files serve.
- `libs/nativeweb.web/README.md` — the pipeline that reads the framework settings.
- `libs/nativeweb.signin/README.md` — the relying party that reads the sign-in settings.
