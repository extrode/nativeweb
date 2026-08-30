namespace __PRODUCT__;

// RFC 9116 security.txt: where a researcher who found a vulnerability should
// report it. Expires is mandatory (the RFC recommends under a year); a stale
// date is this file's way of saying "no longer maintained", so the constant
// below needs a yearly bump -- a visible literal rather than computed from the
// clock, which would defeat the staleness signal.
// Skeleton copy -- the product owns this file after stamping.
//
// The contact is configuration, and unconfigured means NO security.txt is
// served. It was a placeholder literal until 2026-08-29, and a placeholder is
// the worst of the three options: a stamped product shipped
// security@<product>.example, a domain it does not own, so every vulnerability
// report sent there bounced into nobody's inbox and the reporter had no way to
// learn that. Absence tells a researcher to look elsewhere. A placeholder tells
// them they are finished.
internal static class SecurityTxtEndpoint
{
    internal const string ConfigurationKey = "__PRODUCT__:SecurityContact";

    // A year out. Bumped by hand; see the note at the top of the file.
    private const string Expires = "2027-08-01T00:00:00Z";

    /// <summary>
    /// Validates a configured security contact and returns it as an RFC 9116 URI.
    /// </summary>
    /// <remarks>
    /// Pure, so the unit tier can drive it directly. Blank is valid and means
    /// "serve no security.txt"; only a supplied-but-unusable value is a failure.
    /// </remarks>
    internal static bool TryNormalize(string? configured, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(configured))
        {
            return true;
        }

        string trimmed = configured.Trim();

        // The body is assembled by concatenation, so a newline here is field
        // injection: one configuration string could add its own Contact:,
        // Canonical: or Encryption: line to a file that scanners parse and act
        // on. Measured, not assumed -- Uri.TryCreate accepts
        // "https://e.com/r\nContact: mailto:evil@x.net" as a valid absolute URI.
        if (trimmed.AsSpan().ContainsAny('\r', '\n'))
        {
            return false;
        }

        // A bare address is the form people reach for and mailto: is what the
        // RFC requires, so accept the first and emit the second.
        if (!trimmed.Contains(':', StringComparison.Ordinal))
        {
            int at = trimmed.IndexOf('@', StringComparison.Ordinal);
            if (at <= 0 || at == trimmed.Length - 1 || trimmed.Any(char.IsWhiteSpace))
            {
                return false;
            }

            normalized = "mailto:" + trimmed;
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        // The three schemes the RFC's own examples use. Anything else is a typo
        // rather than a way to reach anybody.
        if (parsed.Scheme is not ("mailto" or "https" or "tel"))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>
    /// The security.txt body for a normalized contact URI.
    /// </summary>
    internal static string Render(string contact) =>
        "Contact: " + contact + "\n"
        + "Expires: " + Expires + "\n"
        + "Preferred-Languages: en\n";

    /// <summary>
    /// Maps security.txt, or maps nothing when no contact is configured.
    /// </summary>
    public static void MapSecurityTxtEndpoint(this WebApplication app, string? securityContact)
    {
        if (string.IsNullOrEmpty(securityContact))
        {
            return;
        }

        string body = Render(securityContact);

        // The canonical location is /.well-known/security.txt; the RFC also
        // permits a top-level /security.txt for legacy scanners.
        //
        // Rate-limited like every other non-probe route. Two unauthenticated GETs
        // with no ceiling in an app that attaches a policy everywhere else is not a
        // deliberate exemption, it is the one place the policy was forgotten -- and
        // the framework app attaches one to both of these. /healthz and /readyz are
        // the routes that are exempt on purpose, and they say so where they are.
        app.MapGet("/.well-known/security.txt", () => Results.Text(body, "text/plain"))
            .RequireRateLimiting("general");
        app.MapGet("/security.txt", () => Results.Text(body, "text/plain"))
            .RequireRateLimiting("general");
    }
}
