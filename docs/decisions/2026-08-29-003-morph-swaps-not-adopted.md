# 003 — Morph swaps are not adopted (spec 012, R8)

Date: 2026-08-29
Status: accepted
Supersedes: nothing. Revisit if the swap targets described below stop containing their own forms.

## Decision

htmx 4.0's morph swaps (`innerMorph` / `outerMorph`) are **not used anywhere** in `apps/web` or
the skeleton. Every swap stays on `outerHTML`, which is what 2.0.10 did. No global morph config is
set, and `data-nw-swap` gains no new accepted value.

This is the outcome of an audit, not a decision to defer. Spec 012 R8 asked for selective
adoption — morph where preserved state is a win, never on a form carrying a credential or
re-rendering itself on validation error. Applying that rule to the actual render tree eliminated
every candidate.

## Why every candidate was excluded

There are five distinct swap targets in the app. The exclusion rule reaches all of them:

| Target | Rendered by | Why excluded |
|---|---|---|
| `closest .auth-board` | `SignInPage`, `RegisterPage`, `RegisterVerifyPage`, `PassphraseResetPage`, `PassphraseResetVerifyPage`, `PassphraseChangePage`, `TwoFactorEnrollPage`, `TwoFactorVerifyPage` | Credential forms. Morph preserves input values, so a rejected sign-in would leave the passphrase in the box. |
| `#oauth-consent` | `OAuthAuthorizePage` | Consent decision; the approve and deny forms share the target and re-render together. |
| `#admin-accounts`, `#admin-realms`, `#admin-applications` | `AdminPage` | Each section contains its own forms and its own error banner, and re-renders itself on validation error. |
| `#settings-2fa`, `#settings-profile`, `#settings-passkeys` | `SettingsPage` | Same shape: forms plus banner inside the swapped region. |
| `#settings-sessions` | `SettingsPage:260` | See below — the one target that looked eligible and is not. |

`#settings-sessions` was the only plausible adoption. It is a list plus one button-only form with
no user-typed input, so morph's input preservation looked free. It is not, for two reasons visible
at `apps/web/Render/SettingsPage.cs:263-269`:

- The form carries `{{chrome.AntiforgeryField}}` and `{{chrome.BotTrapFields}}` — hidden inputs
  **inside** the swap target. Morph preserves form input values by name, so a morph would keep the
  pre-swap token and the pre-swap bot-trap canary instead of taking the freshly rendered ones. The
  canary is a signed time value (`libs/nativeweb.security/BotTraps.cs`), so a preserved one goes
  stale and the user's second click is scored as a bot.
- `{{banner}}` is inside the target too, so the section does re-render itself on error. It is
  covered by the exclusion rule directly, not by a judgement call.

## Consequence

R8 is satisfied by a recorded negative rather than by code. Spec 012's acceptance criterion for
R8 — "auth and consent pages confirmed unchanged" — holds trivially, since nothing changed
anywhere. The e2e assertion 012 T8 planned for a morphed target has no target to assert against
and was replaced by a credential-field-empty assertion on the sign-in form
(`FailedSignIn_LeavesTheCredentialFieldEmpty`), which tests the same property from the excluded
side: that the swap does **not** preserve what was typed.

The thing that would change this decision is a swap target that is pure output — a list, a table,
a chart — with no form, no hidden field and no banner inside it. None exists today. A settings
section that moved its form outside its swapped region would become one.
