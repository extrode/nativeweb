# V5 wiring helper (R23, R42): inserts the BotTrapGuard check after every
# devmarks POST form read, each returning that endpoint's fake success.
# Safe to delete after V5 merges.
import sys

changed = []


def rewrite(path, subs):
    s = open(path, encoding='utf-8').read()
    orig = s
    for old, new in subs:
        if old not in s:
            print('MISS in %s: %s' % (path, old[:70].replace('\n', '\\n')))
            continue
        if s.count(old) != 1:
            print('AMBIGUOUS in %s: %s' % (path, old[:70].replace('\n', '\\n')))
            continue
        s = s.replace(old, new)
    if s != orig:
        open(path, 'w', encoding='utf-8', newline='\n').write(s)
        changed.append(path)


rewrite('apps/devmarks/Auth/SignInEndpoints.cs', [
    ("""        string passphrase = form["passphrase"].ToString();
""",
     """        string passphrase = form["passphrase"].ToString();

        if (BotTrapGuard.Tripped(context, form))
        {
            // Indistinguishable from a wrong passphrase (R23).
            return FailureResult(context, assets, antiforgery, username);
        }
"""),
    ("""    private static async Task<IResult> PostSignOut(HttpContext context, IAntiforgery antiforgery, CancellationToken ct)
    {
""",
     """    // Deliberately not bot-trapped (see BotTrapGuard): a faked sign-out would
    // leave a real session alive while claiming it ended.
    private static async Task<IResult> PostSignOut(HttpContext context, IAntiforgery antiforgery, CancellationToken ct)
    {
"""),
])

rewrite('apps/devmarks/Auth/RegisterEndpoints.cs', [
    ("""        string confirmPassphrase = form["confirmPassphrase"].ToString();
""",
     """        string confirmPassphrase = form["confirmPassphrase"].ToString();

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success redirect, with no account created (R23).
            return SignInEndpoints.RedirectResult(context, "/");
        }
"""),
])

rewrite('apps/devmarks/Auth/SettingsEndpoints.cs', [
    ("""        Users.Result.GetHashedPassphrase? hashRow""",
     """        if (BotTrapGuard.Tripped(context, form))
        {
            // The success banner, with the passphrase untouched (R23).
            return KeyedResult(context, assets, antiforgery, user, "settings.password_changed", ok: true);
        }

        Users.Result.GetHashedPassphrase? hashRow"""),
])

rewrite('apps/devmarks/Bookmarks/BookmarkEndpoints.cs', [
    ("""        bool manual = form.ContainsKey("manual");
""",
     """        bool manual = form.ContainsKey("manual");

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success redirect, with nothing saved (R23).
            return SignInEndpoints.RedirectResult(context, "/bookmarks");
        }
"""),
    ("""        string tagsInput = form["tags"].ToString();
        TagRules.ParsedTags tags = TagRules.ParseTagInput(tagsInput);

""",
     """        string tagsInput = form["tags"].ToString();
        TagRules.ParsedTags tags = TagRules.ParseTagInput(tagsInput);

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success redirect, with the bookmark unchanged (R23).
            return SignInEndpoints.RedirectResult(context, "/bookmarks");
        }

"""),
    ("""        // Deleting an id that isn't the caller's affects no rows""",
     """        IFormCollection form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
        if (BotTrapGuard.Tripped(context, form))
        {
            // The success redirect, with the bookmark left standing (R23).
            return SignInEndpoints.RedirectResult(context, "/bookmarks");
        }

        // Deleting an id that isn't the caller's affects no rows"""),
])

rewrite('apps/devmarks/Tags/TagEndpoints.cs', [
    ("""        string name = form["name"].ToString().Trim().ToLowerInvariant();
""",
     """        string name = form["name"].ToString().Trim().ToLowerInvariant();

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success redirect, with the tag untouched (R23).
            return SignInEndpoints.RedirectResult(context, "/tags");
        }
"""),
    ("""        bool isPublic = form["public"].ToString() == "1";
""",
     """        bool isPublic = form["public"].ToString() == "1";

        if (BotTrapGuard.Tripped(context, form))
        {
            // The success redirect, with visibility unchanged (R23).
            return SignInEndpoints.RedirectResult(context, "/tags");
        }
"""),
    ("""        // Someone else's id deletes nothing""",
     """        IFormCollection form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
        if (BotTrapGuard.Tripped(context, form))
        {
            // The success redirect, with the tag left standing (R23).
            return SignInEndpoints.RedirectResult(context, "/tags");
        }

        // Someone else's id deletes nothing"""),
])

rewrite('apps/devmarks/Transfer/TransferEndpoints.cs', [
    ("""        IReadOnlyList<TransferBookmark> parsed;
""",
     """        // ReadImportContentAsync already parsed and cached the form, so this
        // re-read is free.
        IFormCollection form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
        if (BotTrapGuard.Tripped(context, form))
        {
            // A plausible empty report, with nothing written (R23).
            return ReportResult(context, assets, antiforgery, created: 0, updated: 0, skippedInvalidUrl: 0, droppedTags: 0);
        }

        IReadOnlyList<TransferBookmark> parsed;
"""),
    ("""        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NativeWeb.DevMarks.Transfer");
        LogImport(logger, userId, created, updated, skippedInvalidUrl, droppedTags);

        var chrome = Chrome.For(context, assets, antiforgery);
        LocaleCatalog locale = chrome.Locale;
        int totalImported = created + updated;""",
     """        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NativeWeb.DevMarks.Transfer");
        LogImport(logger, userId, created, updated, skippedInvalidUrl, droppedTags);

        return ReportResult(context, assets, antiforgery, created, updated, skippedInvalidUrl, droppedTags);
    }

    private static IResult ReportResult(
        HttpContext context, AssetUrls assets, IAntiforgery antiforgery,
        int created, int updated, int skippedInvalidUrl, int droppedTags)
    {
        var chrome = Chrome.For(context, assets, antiforgery);
        LocaleCatalog locale = chrome.Locale;
        int totalImported = created + updated;"""),
])

rewrite('apps/devmarks/Program.cs', [
    ("""        builder.Services.AddAntiforgery();
""",
     """        builder.Services.AddAntiforgery();

        // Bot-trap timing (R21). MinAge trips on sub-3s submits, so the test
        // tiers set DevMarks:BotTraps:MinAgeSeconds=0 -- their submits are
        // machine-fast by construction.
        var botTrapOptions = new BotTrapOptions();
        if (builder.Configuration["DevMarks:BotTraps:MinAgeSeconds"] is { Length: > 0 } minAgeRaw)
        {
            botTrapOptions.MinAge = TimeSpan.FromSeconds(double.Parse(minAgeRaw, CultureInfo.InvariantCulture));
        }
        builder.Services.AddSingleton(botTrapOptions);
"""),
    ("""using System.Threading.RateLimiting;
""",
     """using System.Globalization;
using System.Threading.RateLimiting;
"""),
    ("""using NativeWeb.Render;
""",
     """using NativeWeb.Render;
using NativeWeb.Security;
"""),
])

print('changed:', changed)
if len(changed) != 7:
    sys.exit(1)
