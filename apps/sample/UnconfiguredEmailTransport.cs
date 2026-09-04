using NativeWeb.Mail;

namespace NativeWeb.Sample;

/// <summary>
/// The transport an app with no SMTP configuration gets.
/// </summary>
/// <remarks>
/// It throws, which is how <see cref="IEmailTransport"/> reports a failed send, and
/// the contact form catches it and tells the visitor their message could not be sent
/// (spec 027 R9). The alternatives are both worse: registering nothing makes the
/// first contact submission a 500 from the container, and registering a transport
/// that silently succeeds tells a visitor their message arrived somewhere when it
/// went nowhere.
/// </remarks>
internal sealed class UnconfiguredEmailTransport : IEmailTransport
{
    public Task SendAsync(string toAddress, EmailContent content, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No SMTP host is configured, so there is nowhere to send this. Set Sample:Smtp:Host.");
}
