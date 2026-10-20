namespace NativeWeb.Mail;

/// <summary>
/// Sends one rendered email to one recipient.
/// </summary>
/// <remarks>
/// <para>
/// The seam a host substitutes. <see cref="SmtpTransport"/> is the one implementation
/// the framework ships; anything that wants mail without a mail server -- a test, a
/// development host, an application that queues instead of sending -- provides its
/// own rather than reaching for a network stub.
/// </para>
/// <para>
/// It is deliberately one method wide. Everything a transport could otherwise be
/// asked for -- rendering, recipient parsing, retry -- already lives elsewhere in
/// this library or above it, and a seam that grows past what a substitute can
/// plausibly implement stops being substitutable. Spec 027 R8.
/// </para>
/// </remarks>
public interface IEmailTransport
{
    /// <summary>
    /// Sends <paramref name="content"/> to <paramref name="toAddress"/>.
    /// </summary>
    /// <param name="toAddress">Recipient address.</param>
    /// <param name="content">The rendered subject and bodies to send.</param>
    /// <param name="cancellationToken">Cancellation token for the send.</param>
    /// <exception cref="Exception">
    /// An implementation reports a failed send by throwing. Which exception is its
    /// own business: a caller that must not fail the request over a mail server
    /// catches broadly and says so, which is what spec 027 R9 has the sample do.
    /// </exception>
    Task SendAsync(string toAddress, EmailContent content, CancellationToken cancellationToken);
}
