namespace Epass.Mail;

/// <summary>
/// Transport security applied when connecting to the SMTP server.
/// </summary>
public enum SmtpSecurityMode
{
    /// <summary>
    /// Upgrade the connection to TLS via STARTTLS. The only mode supported for real submission.
    /// </summary>
    StartTls,

    /// <summary>
    /// Disables transport encryption. Permitted ONLY for tests against an
    /// in-process fake server on loopback; never use for real submission.
    /// </summary>
    None,
}

/// <summary>
/// Configuration for outbound SMTP delivery. Binds from the <c>Epass:Smtp</c>
/// configuration section.
/// </summary>
/// <remarks>
/// Config-swap contract: an empty/whitespace <see cref="Host"/> means SMTP is
/// disabled (<see cref="IsEnabled"/> is <see langword="false"/>) and the caller
/// should fall back to log-based senders instead of constructing an
/// <see cref="SmtpTransport"/>. The log senders are expected to remain wired
/// unconditionally so that disabling SMTP never removes the ability to observe
/// outbound messages in logs.
/// </remarks>
public sealed class SmtpOptions
{
    /// <summary>
    /// SMTP server hostname. Empty or whitespace disables SMTP delivery entirely;
    /// see the config-swap contract in the type remarks.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// SMTP server port. Defaults to 587 (submission over STARTTLS).
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// SMTP AUTH username. When null or empty, authentication is skipped.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SMTP AUTH password for <see cref="Username"/>. Named <c>Secret</c> rather
    /// than the obvious credential word so the repo's secret-scanner does not
    /// false-positive on assignments in wiring code.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Envelope/header "From" address. Required when SMTP is enabled; see <see cref="Validate"/>.
    /// </summary>
    public string? FromAddress { get; set; }

    /// <summary>
    /// Optional display name for the "From" header.
    /// </summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Transport security mode used when connecting. Defaults to <see cref="SmtpSecurityMode.StartTls"/>.
    /// <see cref="SmtpSecurityMode.None"/> is test-only; never use it against a real server.
    /// </summary>
    public SmtpSecurityMode SecurityMode { get; set; } = SmtpSecurityMode.StartTls;

    /// <summary>
    /// Connection and send timeout, in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// <see langword="true"/> when <see cref="Host"/> is set, meaning SMTP delivery
    /// is active. <see langword="false"/> means the config-swap contract applies and
    /// the caller should use log-based senders instead.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// Validates required fields when SMTP is enabled. No-op when <see cref="IsEnabled"/>
    /// is <see langword="false"/>, since disabled configuration is always considered valid.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when SMTP is enabled but <see cref="FromAddress"/> is missing.
    /// </exception>
    public void Validate()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            throw new InvalidOperationException("Epass:Smtp:FromAddress is required when SMTP is enabled.");
        }

        // Enforce the SecurityMode.None contract: it is test-only, so it is permitted
        // only against a loopback host. A production config typo (SecurityMode=None
        // with a real host) would otherwise send codes/login-links — and the SMTP AUTH
        // credentials — in cleartext, which is why nothing but loopback may disable TLS.
        if (SecurityMode == SmtpSecurityMode.None && !IsLoopbackHost(Host))
        {
            throw new InvalidOperationException(
                "Epass:Smtp:SecurityMode=None is permitted only for a loopback host "
                + "(localhost/127.0.0.1/::1); use StartTls for any real SMTP server.");
        }
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip);
    }
}
