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
/// Address validation and header-injection defenses shared by e-mail senders.
/// Content itself is rendered by the app through <see cref="IEmailContentRenderer"/>
/// (spec 003 R45) — this library is transport only.
/// </summary>
public static class EmailTemplates
{
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
    public static string Sanitize(string value)
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

}
