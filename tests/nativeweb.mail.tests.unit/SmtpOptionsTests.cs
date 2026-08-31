using NativeWeb.Mail;

using Xunit;

namespace NativeWeb.Mail.Tests.Unit;

/// <summary>
/// Configuration contract for outbound SMTP: the config-swap disable rule, the
/// required From address, and the loopback-only exception for SecurityMode None.
/// </summary>
public sealed class SmtpOptionsTests
{
    [Fact]
    public void Defaults_MatchSubmissionOverStartTls()
    {
        var options = new SmtpOptions();

        Assert.Equal(587, options.Port);
        Assert.Equal(10, options.TimeoutSeconds);
        Assert.Equal(SmtpSecurityMode.StartTls, options.SecurityMode);
        Assert.False(options.IsEnabled);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("smtp.example.test", true)]
    public void IsEnabled_DependsOnHostBeingSet(string? host, bool expected)
    {
        var options = new SmtpOptions { Host = host };

        Assert.Equal(expected, options.IsEnabled);
    }

    [Fact]
    public void Validate_DisabledWithInvalidConfig_DoesNotThrow()
    {
        var options = new SmtpOptions
        {
            Host = "   ",
            FromAddress = null,
            SecurityMode = SmtpSecurityMode.None,
        };

        Assert.Null(Record.Exception(options.Validate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EnabledWithoutFromAddress_ThrowsNamingTheSetting(string? fromAddress)
    {
        var options = new SmtpOptions { Host = "smtp.example.test", FromAddress = fromAddress };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("NativeWeb:Smtp:FromAddress", ex.Message);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LocalHost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("127.192.0.1")]
    [InlineData("::1")]
    public void Validate_NoneOnLoopbackHost_IsAccepted(string host)
    {
        var options = new SmtpOptions
        {
            Host = host,
            FromAddress = "noreply@example.test",
            SecurityMode = SmtpSecurityMode.None,
        };

        Assert.Null(Record.Exception(options.Validate));
    }

    [Theory]
    [InlineData("smtp.example.test")]
    [InlineData("93.184.216.34")]
    [InlineData("10.0.0.1")]
    public void Validate_NoneOnNonLoopbackHost_Throws(string host)
    {
        var options = new SmtpOptions
        {
            Host = host,
            FromAddress = "noreply@example.test",
            SecurityMode = SmtpSecurityMode.None,
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("SecurityMode=None", ex.Message);
    }

    [Theory]
    [InlineData("smtp.example.test")]
    [InlineData("127.0.0.1")]
    [InlineData("mail.example.test")]
    public void Validate_StartTls_IsAcceptedForAnyHost(string host)
    {
        var options = new SmtpOptions
        {
            Host = host,
            FromAddress = "noreply@example.test",
            SecurityMode = SmtpSecurityMode.StartTls,
        };

        Assert.Null(Record.Exception(options.Validate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Constructor_ConfigMissingFromAddress_Throws(string? fromAddress)
    {
        var options = new SmtpOptions { Host = "smtp.example.test", FromAddress = fromAddress };

        Assert.Throws<InvalidOperationException>(() => new SmtpTransport(options));
    }

    [Fact]
    public void Constructor_NoneOnNonLoopbackHost_Throws()
    {
        var options = new SmtpOptions
        {
            Host = "smtp.example.test",
            FromAddress = "noreply@example.test",
            SecurityMode = SmtpSecurityMode.None,
        };

        Assert.Throws<InvalidOperationException>(() => new SmtpTransport(options));
    }

    [Theory]
    [InlineData("127.0.0.1", SmtpSecurityMode.None)]
    [InlineData("smtp.example.test", SmtpSecurityMode.StartTls)]
    public void Constructor_ValidConfig_DoesNotThrowOrConnect(string host, SmtpSecurityMode securityMode)
    {
        var options = new SmtpOptions
        {
            Host = host,
            FromAddress = "noreply@example.test",
            SecurityMode = securityMode,
        };

        Exception? thrown = Record.Exception(() => new SmtpTransport(options));

        Assert.Null(thrown);
    }
}
