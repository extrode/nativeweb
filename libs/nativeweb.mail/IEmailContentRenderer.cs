namespace NativeWeb.Mail;

/// <summary>
/// Renders transactional e-mail content for a recipient, in the recipient's
/// locale (spec 003 R45). Implemented by the app — content lives in the app's
/// templates/emails/ and locales/, not in this transport library. Async because
/// resolving the recipient's stored locale preference may hit the database.
/// </summary>
public interface IEmailContentRenderer
{
    ValueTask<EmailContent> LoginLinkAsync(string recipientEmail, string productName, string linkUrl, CancellationToken cancellationToken);

    ValueTask<EmailContent> OneTimeCodeAsync(string recipientEmail, string productName, string code, CancellationToken cancellationToken);
}
