using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace NativeWeb.Render;

/// <summary>How a request's locale is chosen (spec 003 R16).</summary>
public sealed class LocaleNegotiationOptions
{
    /// <summary>Lowercase locale ids the app ships catalogs for, e.g. ["en", "en-us", "ms"].</summary>
    public required IReadOnlyList<string> SupportedLocales { get; init; }

    public string DefaultLocale { get; init; } = "en";

    public string CookieName { get; init; } = "locale";

    /// <summary>
    /// Signed-in user's stored preference (users.locale), consulted before the
    /// cookie and Accept-Language. Return null when there is no signed-in user
    /// or no stored preference.
    /// </summary>
    public Func<HttpContext, ValueTask<string?>>? UserPreference { get; init; }
}

/// <summary>
/// Chooses the request locale — user preference, then cookie, then
/// Accept-Language, then the default — and parks it in HttpContext.Items for
/// <see cref="GetLocale"/>. Dialects fall back to their base language before the
/// next candidate is tried ("en-gb" matches "en" when only "en" is shipped).
/// </summary>
public static class LocaleNegotiation
{
    private static readonly object ItemKey = new();

    public static IApplicationBuilder UseLocaleNegotiation(this IApplicationBuilder app, LocaleNegotiationOptions options) =>
        app.Use(async (context, next) =>
        {
            context.Items[ItemKey] = await NegotiateAsync(context, options);
            await next(context);
        });

    /// <summary>The negotiated lowercase locale id, or "en" outside the middleware.</summary>
    public static string GetLocale(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out object? value) && value is string locale ? locale : "en";

    internal static async ValueTask<string> NegotiateAsync(HttpContext context, LocaleNegotiationOptions options)
    {
        if (options.UserPreference is not null)
        {
            string? preference = await options.UserPreference(context);
            if (Match(preference, options.SupportedLocales) is string fromUser)
            {
                return fromUser;
            }
        }

        if (context.Request.Cookies.TryGetValue(options.CookieName, out string? cookie)
            && Match(cookie, options.SupportedLocales) is string fromCookie)
        {
            return fromCookie;
        }

        foreach (string candidate in AcceptLanguageOrder(context.Request.Headers.AcceptLanguage.ToString()))
        {
            if (Match(candidate, options.SupportedLocales) is string fromHeader)
            {
                return fromHeader;
            }
        }

        return options.DefaultLocale;
    }

    /// <summary>Exact dialect match first, then the candidate's base language.</summary>
    internal static string? Match(string? candidate, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string normalized = candidate.Trim().ToLowerInvariant();
        if (supported.Contains(normalized))
        {
            return normalized;
        }

        int dash = normalized.IndexOf('-');
        if (dash > 0)
        {
            string baseLanguage = normalized[..dash];
            if (supported.Contains(baseLanguage))
            {
                return baseLanguage;
            }
        }

        return null;
    }

    /// <summary>Accept-Language values ordered by descending q, "*" excluded.</summary>
    internal static IEnumerable<string> AcceptLanguageOrder(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return [];
        }

        List<(string Tag, double Quality)> entries = [];
        foreach (string part in header.Split(','))
        {
            string[] pieces = part.Split(';');
            string tag = pieces[0].Trim();
            if (tag.Length == 0 || tag == "*")
            {
                continue;
            }

            double quality = 1.0;
            for (int i = 1; i < pieces.Length; i++)
            {
                string piece = pieces[i].Trim();
                // RFC 9110 qvalue: plain decimal in [0, 1] — no exponents, no
                // sign — so "q=1e9" is rejected rather than sorted first.
                if (piece.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(piece[2..], System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                    && parsed is >= 0 and <= 1)
                {
                    quality = parsed;
                }
            }

            if (quality > 0)
            {
                entries.Add((tag, quality));
            }
        }

        return entries.OrderByDescending(e => e.Quality).Select(e => e.Tag);
    }
}
