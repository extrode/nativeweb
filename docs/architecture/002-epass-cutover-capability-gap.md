# 002 — The epass.io cutover capability gap

Status: current — measured 2026-08-25
Companion: `../../work/todo.md` ("epass.io cutover readiness"), `001-relying-party-extraction.md`

Promoted out of `work/todo.md` on 2026-08-25 because it outlives the backlog it was written in:
it is the measurement that decides the shape of the cutover, not an item to tick off.

## What was measured

**Nothing in this repo had ever been compared against `C:\home\code\extrode.com\epass.io`, the
repo being replaced.** Every readiness probe so far measured NativeWeb against NativeWeb. Done now,
and the honest summary is that **`apps/web` is not a drop-in replacement for epass.io** — it is a
server-rendered redesign of epass.io's *browser* surface, and epass.io is a JSON API with five
clients hanging off it.

Shape of the incumbent, which explains everything below: epass.io's `apps/api` is the .NET service,
and its `apps/web` is a **SvelteKit/Vite frontend** (`package.json`, `vite.config.ts`) that calls
that API — so it is a client, not a second server. NativeWeb's `apps/web` collapses both into one
server-rendered app, which is the right design and is exactly why the API surface has no home.

**Verified directly here, not taken on report:**

| Capability | epass.io | NativeWeb `apps/web` |
|---|---|---|
| Hosted background services | `CodeSendWorker` + `ExpiredRowReaper` (`apps/api/Program.cs:430,440`) | **none** — no `AddHostedService`/`IHostedService` anywhere in `apps/web` |
| SMS OTP delivery | AWS SNS + Plivo with round-robin failover (`libs/epass.sms`) | **none** — four `ICodeSender`s exist (`ChannelRouting`, `DevLog`, `Resilient`, `Smtp`) and not one sends SMS, though `AuthFlows.cs:27,45` still computes `CodeChannel.Sms` |
| Telegram / WhatsApp linking, OTP, reverse-login | `libs/epass.messaging` + inbound webhooks | **none** — no Telegram or WhatsApp code in the repo |
| WebAuthn | full JSON management API: list/rename/delete credentials, step-up start/complete, enroll start/complete | 4 HTML routes only — `GET`/`POST /auth/webauthn/{enroll,verify}` (`WebAuthnEndpoints.cs:37-51`). No management, no step-up API |
| `/oauth/userinfo` | served | **absent** — the string appears only in a comment (`Program.cs:547`) |
| Native device authenticators + push approvals | `/authenticator/devices/*`, `/authenticator/pending`, `/approvals/{ref}/{start,complete,deny,push}` | **none** |
| TOTP / sessions self-service JSON APIs, internal KV store | served | **none** |

**All five clients break.** Route usage read out of each client's own source:

| Client | Calls | Served by NativeWeb |
|---|---|---|
| `clients/desktop` | `/authenticator/devices`, `/authenticator/pending` | no |
| `clients/desktop-tauri` | `/authenticator/*`, `/approvals/*`, `/ws` | no |
| `clients/mobile` (iOS + Android) | `/token`, `/webauthn/register/{start,complete}`, `/authenticator/devices/`, `/authenticator/pending`, `/authorize` | no |
| `addon/` (browser extension) | `/oauth/userinfo` | no |
| epass.io's own `apps/web` SvelteKit UI | the whole JSON API | no |

**epass.io also has live unmerged work of its own.** `feat/desktop-webauthn-windows` is 8 commits
ahead of its `dev` and cannot merge: it is gated on T6, a live end-to-end run that needs a real TPM
and so must run on machine P2. Its `work/status/next.md` additionally lists production provisioning
for mobile/desktop (APNs key, Firebase project, device-authenticated push-token rotation) and
mobile domain association waiting on an Apple Team ID, an Android signing fingerprint and
production DNS. Its working tree is dirty (`work/todo.md` modified, an untracked plan doc).

**So the cutover is not a deploy exercise, and the question it actually poses is a scope decision
nobody has taken:** does NativeWeb replace all of epass.io, or only its browser surface while
`apps/api` keeps serving the native clients and the addon? Those are very different amounts of
work, and the four deploy blockers above are downstream of whichever answer wins. Nothing should be
scheduled until that is decided.

Not re-verified here and worth checking before acting: the exhaustive route-by-route inventory of
`apps/api`. The capability rows and the client route lists above were each confirmed against the
source; the claim that the API surface is otherwise complete was not.


## Sequencing, noted 2026-08-25

The user's reading on seeing this: **finish epass.io's own outstanding tasks before migrating
anything to NativeWeb.** That is consistent with what is above — `feat/desktop-webauthn-windows`
cannot merge without a live TPM run on machine P2, and the mobile/desktop production provisioning
(APNs key, Firebase project, Apple Team ID, Android signing fingerprint, production DNS) is
unfinished — but it is a sequencing preference, not yet the scope decision. Whole replacement
versus browser-surface-only is still open, and it is the thing that determines whether epass.io's
remaining work is throwaway or permanent.
