using System.Text;

using MimeKit;

namespace NativeWeb.Mail;

/// <summary>
/// Rendered, ready-to-send email content: subject plus plain-text and HTML bodies.
/// </summary>
/// <param name="Subject">Email subject line.</param>
/// <param name="TextBody">Plain-text body.</param>
/// <param name="HtmlBody">HTML body.</param>
public readonly record struct EmailContent(string Subject, string TextBody, string HtmlBody);

/// <summary>
/// Builds sanitized email content for transactional messages, and validates
/// recipient addresses before send.
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// Builds the subject, text and HTML bodies for a login-link sign-in email.
    /// </summary>
    /// <param name="productName">Caller-supplied product name; sanitized before interpolation.</param>
    /// <param name="linkUrl">Sign-in link; sanitized before interpolation.</param>
    public static EmailContent LoginLink(string productName, string linkUrl)
    {
        string product = Sanitize(productName);
        string link = Sanitize(linkUrl);

        string subject = $"Sign in to {product}";

        string text =
            $"Use the link below to sign in to {product}:\n\n" +
            $"{link}\n\n" +
            "This link is valid for 10 minutes.\n\n" +
            "If you did not request this, you can safely ignore this email.";

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body>");
        html.Append($"<p>Use the button below to sign in to {HtmlEncode(product)}:</p>");
        html.Append($"<p><a href=\"{HtmlAttributeEncode(link)}\">Sign in to {HtmlEncode(product)}</a></p>");
        html.Append("<p>This link is valid for 10 minutes.</p>");
        html.Append("<p>If you did not request this, you can safely ignore this email.</p>");
        html.Append("</body></html>");

        return new EmailContent(subject, text, html.ToString());
    }

    /// <summary>
    /// Builds the subject, text and HTML bodies for a one-time verification code email.
    /// </summary>
    /// <param name="productName">Caller-supplied product name; sanitized before interpolation.</param>
    /// <param name="code">The verification code; sanitized before interpolation.</param>
    public static EmailContent OneTimeCode(string productName, string code)
    {
        string product = Sanitize(productName);
        string safeCode = Sanitize(code);

        string subject = $"Your {product} code";

        string text =
            $"Your {product} verification code is:\n\n" +
            $"{safeCode}\n\n" +
            "This code is valid for 10 minutes.\n\n" +
            "Never share this code with anyone.";

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body>");
        html.Append($"<p>Your {HtmlEncode(product)} verification code is:</p>");
        html.Append($"<p style=\"font-size:24px;font-weight:bold;letter-spacing:4px\">{HtmlEncode(safeCode)}</p>");
        html.Append("<p>This code is valid for 10 minutes.</p>");
        html.Append("<p>Never share this code with anyone.</p>");
        html.Append("</body></html>");

        return new EmailContent(subject, text, html.ToString());
    }

    /// <summary>
    /// Validates and parses a recipient address using MimeKit's parser, hardened
    /// with an explicit non-empty-domain check.
    /// </summary>
    /// <remarks>
    /// MimeKit's <see cref="MailboxAddress.TryParse(string, out MailboxAddress?)"/>
    /// is lenient and accepts a bare local part with no domain, so acceptance alone
    /// is not sufficient. This method requires both MimeKit acceptance AND a
    /// non-empty domain part (via <see cref="HasDomain"/>) before considering the
    /// address sendable.
    /// </remarks>
    /// <param name="address">The candidate recipient address.</param>
    /// <returns>The parsed <see cref="MailboxAddress"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when MimeKit rejects the address, or accepts it but it has no domain part.
    /// </exception>
    public static MailboxAddress ParseRecipient(string address)
    {
        // MimeKit's parser is lenient (it accepts a bare local part with no domain),
        // so require MimeKit acceptance AND a non-empty domain part before sending.
        if (!MailboxAddress.TryParse(address, out MailboxAddress? mailbox) ||
            !HasDomain(mailbox.Address))
        {
            throw new ArgumentException($"Invalid email address: '{address}'.", nameof(address));
        }

        return mailbox;
    }

    /// <summary>
    /// Determines whether a MimeKit-parsed address string has a non-empty domain
    /// part after the <c>@</c> separator.
    /// </summary>
    /// <param name="parsedAddress">The address string as returned by MimeKit.</param>
    private static bool HasDomain(string parsedAddress)
    {
        int at = parsedAddress.LastIndexOf('@');
        return at > 0 && at < parsedAddress.Length - 1;
    }

    /// <summary>
    /// Header-injection defense-in-depth: strips CR, LF and other control
    /// characters (except tab) from caller-supplied values before they are
    /// interpolated into subject and body text.
    /// </summary>
    /// <remarks>
    /// MimeKit already encodes header values, but product name is
    /// caller-supplied and flows into both subject and bodies, so this method
    /// removes control characters as a second layer of defense.
    /// </remarks>
    /// <param name="value">The raw, caller-supplied value.</param>
    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c == '\t' || !char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Encodes a string for safe inclusion in HTML text content (and, via
    /// <see cref="HtmlAttributeEncode"/>, attribute values).
    /// </summary>
    /// <param name="value">The value to encode.</param>
    private static string HtmlEncode(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Encodes a string for safe inclusion in an HTML attribute value.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    private static string HtmlAttributeEncode(string value) => HtmlEncode(value);
}
