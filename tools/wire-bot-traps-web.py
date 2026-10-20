# V5 wiring helper (R23, R42): inserts the BotTrapGuard check into every IdP
# form POST handler, each returning that endpoint's fake success.
# Safe to delete after V5 merges.
import sys

changed = []


def rewrite(path, subs, replace_all=()):
    s = open(path, encoding='utf-8').read()
    orig = s
    for old, new in subs:
        n = s.count(old)
        if n == 0:
            print('MISS in %s: %s' % (path, old[:70].replace('\n', '\\n')))
            continue
        if n != 1 and (old, new) not in replace_all:
            print('AMBIGUOUS(%d) in %s: %s' % (n, path, old[:70].replace('\n', '\\n')))
            continue
        s = s.replace(old, new)
    if s != orig:
        open(path, 'w', encoding='utf-8', newline='\n').write(s)
        changed.append(path)


SAFE_FORM = """IFormCollection form = context.Request.HasFormContentType
            ? await context.Request.ReadFormAsync(ct).ConfigureAwait(false)
            : FormCollection.Empty;"""

rewrite('apps/web/Auth/SignInEndpoints.cs', [
    ("""        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(passphrase))
        {
            return InvalidCredentialsResult(context, assets, antiforgery, username);
        }
""",
     """        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(passphrase))
        {
            return InvalidCredentialsResult(context, assets, antiforgery, username);
        }

        if (BotTrapGuard.Tripped(context, form))
        {
            // Indistinguishable from a wrong password (R23).
            return InvalidCredentialsResult(context, assets, antiforgery, username);
        }
"""),
])

rewrite('apps/web/Auth/RegisterEndpoints.cs', [
    ("""        string hashedUsername = UsernameHashing.Hash(normalizedUsername);
""",
     """        string hashedUsername = UsernameHashing.Hash(normalizedUsername);

        if (BotTrapGuard.Tripped(context, form))
        {
            // The verify-step redirect with nothing staged and no email sent:
            // a later verify falls through to "code expired", same as any
            // stale registration (R23).
            return RedirectToVerify(context, hashedUsername);
        }
"""),
    ("""        const string genericError = "This code has expired or is invalid. Please register again.";
""",
     """        const string genericError = "This code has expired or is invalid. Please register again.";

        if (BotTrapGuard.Tripped(context, form))
        {
            // Indistinguishable from an expired or wrong code (R23).
            return VerifyError(context, assets, antiforgery, hashedUsername, genericError);
        }
"""),
])

rewrite('apps/web/Auth/PassphraseEndpoints.cs', [
    ("""        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 320)
        {
            return ResetStartError(context, assets, antiforgery, identifier, "Enter your username or email.");
        }
""",
     """        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 320)
        {
            return ResetStartError(context, assets, antiforgery, identifier, "Enter your username or email.");
        }

        if (BotTrapGuard.Tripped(context, form))
        {
            // The neutral "check your email" shape with nothing staged and no
            // email sent -- the same shape an unknown identifier gets (R23).
            string trapNormalized = identifier.Trim().ToLowerInvariant();
            return RedirectToResetVerify(context, UsernameHashing.Hash(trapNormalized),
                FakeIdentifierResponses.MaskedDestination(contactProtector, trapNormalized));
        }
"""),
    ("""        const string genericError = "This code has expired or is invalid. Please start over.";
""",
     """        const string genericError = "This code has expired or is invalid. Please start over.";

        if (BotTrapGuard.Tripped(context, form))
        {
            // Indistinguishable from an expired or wrong code (R23).
            return ResetVerifyError(context, assets, antiforgery, hashedUsername, maskedDestination, genericError);
        }
"""),
    ("""        string currentPassphrase = form["currentPassphrase"].ToString();
        string newPassphrase = form["newPassphrase"].ToString();
        string confirmPassphrase = form["confirmPassphrase"].ToString();
""",
     """        string currentPassphrase = form["currentPassphrase"].ToString();
        string newPassphrase = form["newPassphrase"].ToString();
        string confirmPassphrase = form["confirmPassphrase"].ToString();

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success banner with the passphrase untouched and every
            // other session left alive (R23).
            return Results.Content(PassphraseChangePage.RenderSuccess(context, assets, antiforgery), "text/html");
        }
"""),
])

rewrite('apps/web/Auth/TwoFactorEndpoints.cs', [
    ("""        if (user.TotpEnabledAt is not null)
        {
            return Results.Content(TwoFactorEnrollPage.RenderAlreadyEnabled(context, assets, antiforgery), "text/html");
        }
""",
     """        if (user.TotpEnabledAt is not null)
        {
            return Results.Content(TwoFactorEnrollPage.RenderAlreadyEnabled(context, assets, antiforgery), "text/html");
        }

        if (BotTrapGuard.Tripped(context, form))
        {
            // Indistinguishable from a wrong authenticator code (R23).
            return Results.Content(
                TwoFactorEnrollPage.Render(context, assets, antiforgery, user.Username, secret, "Incorrect code. Check your authenticator app and try again."),
                "text/html");
        }
"""),
    ("""        string submitted = form["code"].ToString();
""",
     """        string submitted = form["code"].ToString();

        if (BotTrapGuard.Tripped(context, form))
        {
            // Indistinguishable from a wrong code (R23).
            return VerifyError(context, assets, antiforgery, submitted.Contains('-', StringComparison.Ordinal), GenericVerifyError);
        }
"""),
])

rewrite('apps/web/Auth/SettingsEndpoints.cs', [
    ("""        string firstName = form["firstName"].ToString().Trim();
        string lastName = form["lastName"].ToString().Trim();
""",
     """        string firstName = form["firstName"].ToString().Trim();
        string lastName = form["lastName"].ToString().Trim();

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success fragment with nothing saved (R23).
            return Results.Content(SettingsPage.RenderProfileSection(context, assets, antiforgery, firstName, lastName, error: null, success: true), "text/html");
        }
"""),
    ("""        if (user.TotpEnabledAt is not null)
        {
            await db.Users.ClearTotpEnrollmentAsync(userId, ct).ConfigureAwait(false);""",
     """        %s
        if (BotTrapGuard.Tripped(context, form))
        {
            // The disabled-state section with enrollment untouched (R23).
            return Results.Content(SettingsPage.RenderTwoFactorSection(context, assets, antiforgery, totpEnabled: false, error: null), "text/html");
        }

        if (user.TotpEnabledAt is not null)
        {
            await db.Users.ClearTotpEnrollmentAsync(userId, ct).ConfigureAwait(false);""" % SAFE_FORM),
    ("""        DateTime now = DateTime.UtcNow;
        await db.TotpRecoveryCodes.DeleteByUserIdAsync(userId, ct).ConfigureAwait(false);""",
     """        %s
        if (BotTrapGuard.Tripped(context, form))
        {
            // Codes that look real but were never stored: the real set stays
            // valid and nothing regenerates (R23).
            var trapCodes = new List<string>(TotpPolicy.RecoveryCodeCount);
            for (int i = 0; i < TotpPolicy.RecoveryCodeCount; i++)
            {
                trapCodes.Add(GenerateRecoveryCode());
            }

            return Results.Content(SettingsPage.RenderRecoveryCodesSection(context, assets, antiforgery, trapCodes), "text/html");
        }

        DateTime now = DateTime.UtcNow;
        await db.TotpRecoveryCodes.DeleteByUserIdAsync(userId, ct).ConfigureAwait(false);""" % SAFE_FORM),
    ("""        List<WebauthnCredentials.Result.GetActiveByUserId> existing = await db.WebauthnCredentials.GetActiveByUserIdAsync(userId, ct).ConfigureAwait(false);""",
     """        %s
        if (BotTrapGuard.Tripped(context, form))
        {
            // The success shape -- the row is omitted from the response but
            // nothing is deleted (R23).
            IReadOnlyList<SettingsPage.PasskeyRow> kept = (await LoadPasskeysAsync(db, userId, ct).ConfigureAwait(false))
                .Where(p => p.Id != id).ToList();
            return Results.Content(SettingsPage.RenderPasskeysSection(context, assets, antiforgery, kept, error: null), "text/html");
        }

        List<WebauthnCredentials.Result.GetActiveByUserId> existing = await db.WebauthnCredentials.GetActiveByUserIdAsync(userId, ct).ConfigureAwait(false);""" % SAFE_FORM),
    ("""        await db.BffStore.DeleteAllByUserIdExceptIdAsync(userId, currentSessionId, ct).ConfigureAwait(false);""",
     """        %s
        if (BotTrapGuard.Tripped(context, form))
        {
            // The success shape listing only this session, with every other
            // session left alive (R23).
            IReadOnlyList<SettingsPage.SessionRow> kept = (await LoadSessionsAsync(context, db, userId, ct).ConfigureAwait(false))
                .Where(s => s.IsCurrent).ToList();
            return Results.Content(SettingsPage.RenderSessionsSection(context, assets, antiforgery, kept, error: null, success: true), "text/html");
        }

        await db.BffStore.DeleteAllByUserIdExceptIdAsync(userId, currentSessionId, ct).ConfigureAwait(false);""" % SAFE_FORM),
])

OAUTH_TRAP = ("""        string state = form["state"].ToString();
""",
              """        string state = form["state"].ToString();

        if (BotTrapGuard.Tripped(context, form))
        {
            // The generic decision-error fragment: no grant recorded, no
            // redirect back to the client (R23).
            return Results.Content(OAuthAuthorizePage.RenderErrorFragment(DecisionErrorMessage), "text/html");
        }
""")
rewrite('apps/web/Auth/OAuthEndpoints.cs', [OAUTH_TRAP], replace_all=(OAUTH_TRAP,))

# AdminEndpoints: force/clear halves are textually identical up to the DB verb,
# so split at the clear handler and patch each half.
p = 'apps/web/Auth/AdminEndpoints.cs'
s = open(p, encoding='utf-8').read()
marker = 'private static async Task<IResult> PostClearPassphraseReset'
assert s.count(marker) == 1
head, tail = s.split(marker, 1)

USERNAME_BLOCK = """        if (usernameRaw.Length == 0)
        {
            return Results.Content(
                AdminPage.RenderAccountsSection(context, assets, antiforgery, realms, "Enter a username.", success: null,
                    formUsername: usernameRaw, formRealmId: realmId),
                "text/html");
        }
"""


def account_trap(message):
    return USERNAME_BLOCK + """
        if (BotTrapGuard.Tripped(context, form))
        {
            // The success banner with the account untouched (R23).
            return Results.Content(
                AdminPage.RenderAccountsSection(context, assets, antiforgery, realms, error: null,
                    success: $"{usernameRaw} in {realm.Name} %s",
                    formUsername: "", formRealmId: realmId),
                "text/html");
        }
""" % message


assert head.count(USERNAME_BLOCK) == 1, head.count(USERNAME_BLOCK)
head = head.replace(USERNAME_BLOCK, account_trap("must set a new password before doing anything else."))
assert tail.count(USERNAME_BLOCK) == 1, tail.count(USERNAME_BLOCK)
tail = tail.replace(USERNAME_BLOCK, account_trap("is no longer required to set a new password."))
open(p, 'w', encoding='utf-8', newline='\n').write(head + marker + tail)
changed.append(p)

REALM_TRAP = """
        if (BotTrapGuard.Tripped(context, form))
        {
            // The success shape with the realm list untouched (R23).
            IReadOnlyList<AdminPage.RealmRow> trapRealms = await LoadRealmsAsync(db, ct).ConfigureAwait(false);
            return Results.Content(AdminPage.RenderRealmsSection(context, assets, antiforgery, trapRealms, error: null, success: true), "text/html");
        }
"""

APPS_TRAP_TAIL = """            IReadOnlyList<AdminPage.RealmRow> trapRealms = await LoadRealmsAsync(db, ct).ConfigureAwait(false);
            IReadOnlyList<AdminPage.ApplicationRow> trapApps = await LoadApplicationsAsync(db, ct).ConfigureAwait(false);
"""

rewrite('apps/web/Auth/AdminEndpoints.cs', [
    ("""        string slug = form["slug"].ToString().Trim().ToLowerInvariant();
        string ssoModeRaw = form["ssoMode"].ToString().Trim();
        string ssoMode = string.IsNullOrEmpty(ssoModeRaw) ? SsoModes.Isolated : ssoModeRaw;
        string maxUsersRaw = form["maxUsers"].ToString().Trim();

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);
""",
     """        string slug = form["slug"].ToString().Trim().ToLowerInvariant();
        string ssoModeRaw = form["ssoMode"].ToString().Trim();
        string ssoMode = string.IsNullOrEmpty(ssoModeRaw) ? SsoModes.Isolated : ssoModeRaw;
        string maxUsersRaw = form["maxUsers"].ToString().Trim();

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);
""" + REALM_TRAP),
    ("""        string name = form["name"].ToString().Trim();
        string ssoModeRaw = form["ssoMode"].ToString().Trim();
        string ssoMode = string.IsNullOrEmpty(ssoModeRaw) ? SsoModes.Isolated : ssoModeRaw;
        string maxUsersRaw = form["maxUsers"].ToString().Trim();

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);
""",
     """        string name = form["name"].ToString().Trim();
        string ssoModeRaw = form["ssoMode"].ToString().Trim();
        string ssoMode = string.IsNullOrEmpty(ssoModeRaw) ? SsoModes.Isolated : ssoModeRaw;
        string maxUsersRaw = form["maxUsers"].ToString().Trim();

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);
""" + REALM_TRAP),
    ("""        string? imageUri = imageUriRaw.Length == 0 ? null : imageUriRaw;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);
""",
     """        string? imageUri = imageUriRaw.Length == 0 ? null : imageUriRaw;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        if (BotTrapGuard.Tripped(context, form))
        {
            // A plausible validation error; nothing is created and no secret
            // exists to reveal (R23).
""" + APPS_TRAP_TAIL + """            return Results.Content(AdminPage.RenderApplicationsSection(context, assets, antiforgery, trapApps, trapRealms, "That name is already taken.", secretReveal: null), "text/html");
        }
"""),
    ("""        Applications.Result.Disable? disabled = await db.Applications.DisableAsync(clientId, ct).ConfigureAwait(false);""",
     """        %s
        if (BotTrapGuard.Tripped(context, form))
        {
            // The success banner with the application left enabled (R23).
""" % SAFE_FORM + APPS_TRAP_TAIL + """            return Results.Content(AdminPage.RenderApplicationsSection(context, assets, antiforgery, trapApps, trapRealms, error: null, secretReveal: null, "Application disabled."), "text/html");
        }

        Applications.Result.Disable? disabled = await db.Applications.DisableAsync(clientId, ct).ConfigureAwait(false);"""),
    ("""        string secret = GenerateSecret();""",
     """        %s
        if (BotTrapGuard.Tripped(context, form))
        {
            // A plausible refusal; the real secret is not rotated (R23).
""" % SAFE_FORM + APPS_TRAP_TAIL + """            return Results.Content(AdminPage.RenderApplicationsSection(context, assets, antiforgery, trapApps, trapRealms, "Cannot rotate secret for this application.", secretReveal: null), "text/html");
        }

        string secret = GenerateSecret();"""),
    ("""        bool allowHeadlessAuth = string.Equals(form["allowHeadlessAuth"].ToString(), "on", StringComparison.OrdinalIgnoreCase);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);
""",
     """        bool allowHeadlessAuth = string.Equals(form["allowHeadlessAuth"].ToString(), "on", StringComparison.OrdinalIgnoreCase);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success banner with the assignment untouched (R23).
""" + APPS_TRAP_TAIL + """            return Results.Content(AdminPage.RenderApplicationsSection(context, assets, antiforgery, trapApps, trapRealms, error: null, secretReveal: null, "Application reassigned."), "text/html");
        }
"""),
])

print('changed:', sorted(set(changed)))
if len(set(changed)) != 7:
    sys.exit(1)
