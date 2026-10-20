using NativeWeb.Mail;

using Xunit;

namespace NativeWeb.Mail.Tests.Unit;

/// <summary>
/// Spec 027 R8. The seam a host substitutes, and the one implementation the framework
/// ships behind it.
/// </summary>
public sealed class EmailTransportSeamTests
{
    private static readonly EmailContent Content = new("Subject", "text", "<p>html</p>");

    [Fact]
    public void SmtpTransport_IsAnEmailTransport()
    {
        var options = new SmtpOptions { Host = "smtp.example.test", FromAddress = "noreply@example.test" };

        Assert.IsAssignableFrom<IEmailTransport>(new SmtpTransport(options));
    }

    [Fact]
    public async Task ASubstituteReceivesWhatTheCallerSent()
    {
        var recording = new RecordingTransport();
        IEmailTransport transport = recording;

        await transport.SendAsync("someone@example.test", Content, CancellationToken.None);

        Assert.Equal("someone@example.test", recording.ToAddress);
        Assert.Equal(Content, recording.Content);
    }

    [Fact]
    public async Task ASubstituteReportsAFailedSendByThrowing()
    {
        IEmailTransport transport = new RefusingTransport();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendAsync("someone@example.test", Content, CancellationToken.None));
    }

    private sealed class RecordingTransport : IEmailTransport
    {
        internal string? ToAddress { get; private set; }

        internal EmailContent Content { get; private set; }

        public Task SendAsync(string toAddress, EmailContent content, CancellationToken cancellationToken)
        {
            ToAddress = toAddress;
            Content = content;
            return Task.CompletedTask;
        }
    }

    private sealed class RefusingTransport : IEmailTransport
    {
        public Task SendAsync(string toAddress, EmailContent content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no mail server here");
    }
}
