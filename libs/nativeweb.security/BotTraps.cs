using System.Globalization;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NativeWeb.Security;

/// <summary>Tuning knobs for the render-timestamp canary (R21).</summary>
public sealed class BotTrapOptions
{
    /// <summary>A form submitted faster than a human can read it (default 3 s).</summary>
    public TimeSpan MinAge { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>A form held longer than any real session plausibly lasts (default 24 h).</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>Why a submission was judged non-human. Recorded for tuning (R23), never shown.</summary>
public readonly record struct BotTrapVerdict(bool Tripped, string? Reason)
{
    public static readonly BotTrapVerdict Clean = new(false, null);

    public static BotTrapVerdict Trip(string reason) => new(true, reason);
}

/// <summary>
/// Form bot traps (R21–R24): four honeypot fields plus a signed
/// render-timestamp canary, emitted into every form and inspected on every
/// POST. Field names are chosen to tempt autofill bots ("contact_website",
/// "fax_number"), never to describe themselves (R22). The offscreen pair is
/// hidden by the bt-extra stylesheet class, NOT by inline style or
/// type=hidden, because the classic regression is a sanitizer or CSS change
/// quietly un-hiding them (R24 pins that with an e2e computed-visibility
/// check). A tripped trap must take the endpoint's normal-outcome path with
/// no side effects, and the caller logs the verdict via
/// <see cref="LogTripped"/>.
/// </summary>
public static partial class BotTraps
{
    /// <summary>type=hidden, pre-filled; trips when altered or emptied.</summary>
    public const string HiddenFilledName = "contact_website";
    public const string HiddenFilledValue = "https://";

    /// <summary>type=hidden, empty; trips when filled.</summary>
    public const string HiddenEmptyName = "fax_number";

    /// <summary>Offscreen text input, pre-filled; trips when altered or emptied.</summary>
    public const string OffscreenFilledName = "company_website";
    public const string OffscreenFilledValue = "https://";

    /// <summary>Offscreen text input, empty; trips when filled.</summary>
    public const string OffscreenEmptyName = "phone_extension";

    /// <summary>The signed render-timestamp canary. Named like plumbing, not like a trap.</summary>
    public const string CanaryName = "form_session";

    /// <summary>The stylesheet class that moves the offscreen pair out of view (R22).</summary>
    public const string OffscreenClass = "bt-extra";

    private const string ProtectorPurpose = "NativeWeb.BotTraps.Canary";

    /// <summary>
    /// Issues the signed canary value for a form being rendered now. The
    /// payload is the render instant; expiry enforces MaxAge, the embedded
    /// instant enforces MinAge.
    ///
    /// Known bypass, accepted by design: the canary is bound to the app, not
    /// to a session, path, or single use, so one canary harvested from any
    /// form GET satisfies the timing check on every form until MaxAge runs
    /// out. The mechanism targets bots that fetch-and-post atomically (the
    /// overwhelming spam pattern); a bot tooled up to harvest and age canaries
    /// has graduated past what honeypot fields can catch either.
    /// </summary>
    public static string IssueCanary(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BotTrapOptions options = ResolveOptions(context);
        ITimeLimitedDataProtector protector = ResolveProtector(context);
        DateTimeOffset now = ResolveTime(context).GetUtcNow();
        return protector.Protect(
            now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            now + options.MaxAge);
    }

    /// <summary>
    /// The full trap block as markup, for renderers that build HTML in C#
    /// (the IdP's inline pages). Template-based apps mirror this markup in
    /// their shared/bot-traps partial instead.
    /// </summary>
    public static string Fields(HttpContext context)
    {
        string canary = HtmlEncoder.Default.Encode(IssueCanary(context));
        return $"""
            <input type="hidden" name="{HiddenFilledName}" value="{HiddenFilledValue}" autocomplete="off">
            <input type="hidden" name="{HiddenEmptyName}" value="" autocomplete="off">
            <div class="{OffscreenClass}" aria-hidden="true">
            <label>Company website <input type="text" name="{OffscreenFilledName}" value="{OffscreenFilledValue}" autocomplete="off" tabindex="-1"></label>
            <label>Phone extension <input type="text" name="{OffscreenEmptyName}" value="" autocomplete="off" tabindex="-1"></label>
            </div>
            <input type="hidden" name="{CanaryName}" value="{canary}">
            """;
    }

    /// <summary>
    /// Judges a submitted form. Callers take the endpoint's normal-outcome
    /// path with no side effects when this trips (R23), after logging via
    /// <see cref="LogTripped"/>.
    /// </summary>
    public static BotTrapVerdict Inspect(HttpContext context, IFormCollection form)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (form[HiddenFilledName].ToString() != HiddenFilledValue)
        {
            return BotTrapVerdict.Trip($"honeypot:{HiddenFilledName}");
        }

        if (form[HiddenEmptyName].ToString().Length != 0)
        {
            return BotTrapVerdict.Trip($"honeypot:{HiddenEmptyName}");
        }

        if (form[OffscreenFilledName].ToString() != OffscreenFilledValue)
        {
            return BotTrapVerdict.Trip($"honeypot:{OffscreenFilledName}");
        }

        if (form[OffscreenEmptyName].ToString().Length != 0)
        {
            return BotTrapVerdict.Trip($"honeypot:{OffscreenEmptyName}");
        }

        string canary = form[CanaryName].ToString();
        if (canary.Length == 0)
        {
            return BotTrapVerdict.Trip("canary:missing");
        }

        string payload;
        try
        {
            payload = ResolveProtector(context).Unprotect(canary);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Forged, corrupted, or older than MaxAge — the time-limited
            // protector throws for all three.
            return BotTrapVerdict.Trip("canary:invalid-or-expired");
        }

        if (!long.TryParse(payload, NumberStyles.None, CultureInfo.InvariantCulture, out long issuedMs))
        {
            return BotTrapVerdict.Trip("canary:invalid-or-expired");
        }

        BotTrapOptions options = ResolveOptions(context);
        var issued = DateTimeOffset.FromUnixTimeMilliseconds(issuedMs);
        DateTimeOffset now = ResolveTime(context).GetUtcNow();
        if (now - issued < options.MinAge)
        {
            return BotTrapVerdict.Trip("canary:too-fast");
        }

        return BotTrapVerdict.Clean;
    }

    /// <summary>R23: the structured trap event, one line per tripped submission.</summary>
    public static void LogTripped(ILogger logger, BotTrapVerdict verdict, string path)
    {
        if (verdict.Tripped)
        {
            LogTrapEvent(logger, verdict.Reason!, path);
        }
    }

    private static ITimeLimitedDataProtector ResolveProtector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(ProtectorPurpose)
            .ToTimeLimitedDataProtector();

    private static BotTrapOptions ResolveOptions(HttpContext context) =>
        context.RequestServices.GetService<BotTrapOptions>() ?? new BotTrapOptions();

    private static TimeProvider ResolveTime(HttpContext context) =>
        context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

    [LoggerMessage(Level = LogLevel.Information, Message = "Bot trap tripped: {Reason} on {Path}")]
    private static partial void LogTrapEvent(ILogger logger, string reason, string path);
}
