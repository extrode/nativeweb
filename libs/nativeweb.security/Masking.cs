using System.Text;

namespace NativeWeb.Security;

/// <summary>Redacts contact destinations (email/phone) for display without revealing the full value.</summary>
public static class Masking
{
    private const string Bullets = "•••";

    /// <summary>
    /// Masks an email address, e.g. "alice@example.test" -&gt; "a•••@e•••.test": the
    /// first character of the local part and of each domain label is kept, and the
    /// top-level label is left intact for recognizability.
    /// </summary>
    public static string Email(string email)
    {
        ArgumentException.ThrowIfNullOrEmpty(email);

        int at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == email.Length - 1)
        {
            // Not a shape we can mask meaningfully; degrade to a fully masked token.
            return MaskLabel(email);
        }

        string local = email[..at];
        string domain = email[(at + 1)..];

        var sb = new StringBuilder();
        sb.Append(MaskLabel(local)).Append('@');

        string[] labels = domain.Split('.');
        for (int i = 0; i < labels.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('.');
            }

            // Keep the final label (the TLD) intact; mask the rest.
            sb.Append(i == labels.Length - 1 ? labels[i] : MaskLabel(labels[i]));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Masks a phone number, e.g. "+15550000002" -&gt; "+1 (•••) •••-0002": the last
    /// 4 digits stay visible, everything before them is masked, and a leading
    /// country code digit is kept when present.
    /// </summary>
    public static string Phone(string phone)
    {
        ArgumentException.ThrowIfNullOrEmpty(phone);

        var digits = new StringBuilder(phone.Length);
        foreach (char c in phone)
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
            }
        }

        string d = digits.ToString();
        string last4 = d.Length >= 4 ? d[^4..] : d;
        string country = d.Length > 10 ? d[..^10] : "1";

        return $"+{country} ({Bullets}) {Bullets}-{last4}";
    }

    /// <summary>
    /// Masks an IP address for display, e.g. "203.0.113.42" -&gt; "203.0.113.x" (IPv4,
    /// last octet dropped) or "2001:db8::1" -&gt; "2001:db8::x" (IPv6, last colon
    /// segment dropped). Falls back to a fully masked token for unrecognized shapes.
    /// </summary>
    public static string IpAddress(string ip)
    {
        ArgumentException.ThrowIfNullOrEmpty(ip);

        char separator = ip.Contains(':', StringComparison.Ordinal) ? ':' : '.';
        int lastSep = ip.LastIndexOf(separator);
        if (lastSep <= 0 || lastSep == ip.Length - 1)
        {
            return Bullets;
        }

        return string.Concat(ip.AsSpan(0, lastSep + 1), "x");
    }

    private static string MaskLabel(string label)
    {
        if (label.Length == 0)
        {
            return Bullets;
        }

        return string.Concat(label.AsSpan(0, 1), Bullets);
    }
}
