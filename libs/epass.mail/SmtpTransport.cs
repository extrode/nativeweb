using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Epass.Mail;

/// <summary>
/// Sends a single email over SMTP using MailKit, opening a fresh connection per send.
/// </summary>
public sealed class SmtpTransport
{
    private readonly SmtpOptions _options;

    /// <summary>
    /// Creates the transport and validates <paramref name="options"/> immediately,
    /// so misconfiguration fails at construction rather than at first send.
    /// </summary>
    /// <param name="options">SMTP configuration. Must have <see cref="SmtpOptions.IsEnabled"/> honored by the caller before constructing this type.</param>
    public SmtpTransport(SmtpOptions options)
    {
        _options = options;
        _options.Validate();
    }

    /// <summary>
    /// Connects, authenticates (if configured), sends, and disconnects, all
    /// within this call. A new <see cref="MailKit.Net.Smtp.SmtpClient"/> connection
    /// is opened per send rather than pooled; transactional send volume tolerates
    /// this, and pooling is a later optimization if send rate grows.
    /// </summary>
    /// <remarks>
    /// <see cref="SmtpOptions.SecurityMode"/> of <see cref="SmtpSecurityMode.None"/>
    /// connects with no transport encryption at all; it is test-only, for use
    /// against an in-process fake server on loopback, and must never be selected
    /// for real submission.
    /// </remarks>
    /// <param name="toAddress">Recipient address, validated via <see cref="EmailTemplates.ParseRecipient"/>.</param>
    /// <param name="content">The rendered subject and bodies to send.</param>
    /// <param name="cancellationToken">Cancellation token for the connect/authenticate/send/disconnect sequence.</param>
    public async Task SendAsync(
        string toAddress,
        EmailContent content,
        CancellationToken cancellationToken)
    {
        // Validate() (ctor) guarantees these when enabled; re-assert for the compiler.
        string host = _options.Host ?? throw new InvalidOperationException("SMTP host not configured.");
        string fromAddress = _options.FromAddress ?? throw new InvalidOperationException("SMTP from-address not configured.");

        MailboxAddress recipient = EmailTemplates.ParseRecipient(toAddress);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName ?? string.Empty, fromAddress));
        message.To.Add(recipient);
        message.Subject = content.Subject;

        var body = new BodyBuilder
        {
            TextBody = content.TextBody,
            HtmlBody = content.HtmlBody,
        };
        message.Body = body.ToMessageBody();

        // Per-send connection. Transactional volume tolerates this; connection
        // pooling is a later optimization if send rate grows.
        using var client = new SmtpClient
        {
            Timeout = _options.TimeoutSeconds * 1000,
        };

        SecureSocketOptions socketOptions = _options.SecurityMode == SmtpSecurityMode.None
            ? SecureSocketOptions.None
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(host, _options.Port, socketOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Secret ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}
