using System.Diagnostics.CodeAnalysis;

namespace NativeWeb.SignIn;

/// <summary>
/// The open-redirect guard for the <c>returnTo</c> carried through sign-in.
/// </summary>
/// <remarks>
/// <see cref="Sanitise"/> is deliberately internal. A public sanitiser is a guard a
/// consumer can simply not call, and the one call site that matters — the callback
/// endpoint — is inside this library. What the consumer supplies is the fallback, and
/// that is validated at startup through <see cref="IsUsableFallback"/> so the redirect
/// cannot re-enter through the very parameter meant to close it.
/// </remarks>
public static class ReturnPath
{
    /// <summary>
    /// Returns <paramref name="candidate"/> when it is a same-site relative path,
    /// otherwise <paramref name="fallback"/>. Query and fragment are preserved.
    /// </summary>
    internal static string Sanitise(string? candidate, string fallback) =>
        IsRelativePath(candidate) ? candidate : fallback;

    /// <summary>
    /// Whether a configured fallback is itself a usable relative path. Called during
    /// options validation at startup: a fallback of <c>https://evil.com</c> would make
    /// every rejected candidate redirect off-site, which is the open redirect wearing
    /// the guard's clothes.
    /// </summary>
    /// <param name="fallback">The configured fallback path, or null when none was configured.</param>
    /// <returns>True when the fallback may be used.</returns>
    /// <remarks>
    /// Nullable on purpose: this validates configuration, and unconfigured is the most
    /// common thing configuration is. A non-nullable parameter would push the null case
    /// out to every caller, which is where a startup guard stops being called.
    /// </remarks>
    public static bool IsUsableFallback(string? fallback) => IsRelativePath(fallback);

    private static bool IsRelativePath([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // A single leading forward slash and nothing else: "//evil.com" is
        // protocol-relative and a browser treats it as absolute.
        if (value[0] != '/' || (value.Length > 1 && (value[1] == '/' || value[1] == '\\')))
        {
            return false;
        }

        foreach (char c in value)
        {
            // Backslashes anywhere: browsers normalise "\" to "/" in enough places that
            // "/\evil.com" reaches evil.com. Colons anywhere: rejecting the character is
            // the check that stays correct, where enumerating schemes (http:, javascript:,
            // data:, the next one) is where redirect filters go wrong. A relative path that
            // needs a literal colon can percent-encode it.
            if (c is '\\' or ':' || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
