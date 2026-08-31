using MimeKit;

using NativeWeb.Mail;

using Xunit;

namespace NativeWeb.Mail.Tests.Unit;

/// <summary>
/// Recipient validation and the header-injection scrub shared by every e-mail
/// sender in the framework.
/// </summary>
public sealed class EmailTemplateTests
{
    [Theory]
    [InlineData("alice@example.test")]
    [InlineData("alice@mail.example.test")]
    [InlineData("alice+news@example.test")]
    public void ParseRecipient_SingleAddress_ReturnsSuppliedAddress(string address)
    {
        MailboxAddress mailbox = EmailTemplates.ParseRecipient(address);

        Assert.Equal(address, mailbox.Address);
    }

    [Fact]
    public void ParseRecipient_DisplayName_ReturnsAddressPartNotWholeString()
    {
        MailboxAddress mailbox = EmailTemplates.ParseRecipient("Alice Doe <alice@example.test>");

        Assert.Equal("alice@example.test", mailbox.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alice")]
    [InlineData("alice@")]
    [InlineData("@example.test")]
    [InlineData("@")]
    [InlineData("alice@example.test, bob@example.test")]
    [InlineData("alice@exa\r\nmple.test")]
    [InlineData("alice\n@example.test\nBcc: attacker@example.test")]
    public void ParseRecipient_UnsendableAddress_ThrowsNamingAddressAndParameter(string address)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => EmailTemplates.ParseRecipient(address));

        Assert.Contains(address, ex.Message);
        Assert.Equal("address", ex.ParamName);
    }

    // A line break immediately before the '@' is NOT rejected: MimeKit folds it as
    // header whitespace and parses through it, so these two reach the caller as a
    // clean mailbox rather than as an exception. The contract that matters is the
    // one asserted here -- nothing that comes back carries a line break, so nothing
    // can end a header and start another. Measured 2026-08-31; the shapes MimeKit
    // does reject are in the theory above.
    [Theory]
    [InlineData("alice\n@example.test")]
    [InlineData("alice\r@example.test")]
    public void ParseRecipient_AddressCarryingALineBreak_ReturnsNoLineBreak(string address)
    {
        MailboxAddress mailbox = EmailTemplates.ParseRecipient(address);

        Assert.DoesNotContain('\r', mailbox.Address);
        Assert.DoesNotContain('\n', mailbox.Address);
        Assert.DoesNotContain('\r', mailbox.ToString());
        Assert.DoesNotContain('\n', mailbox.ToString());
    }

    [Theory]
    [InlineData("a\rb", "ab")]
    [InlineData("a\nb", "ab")]
    [InlineData("a\r\nb", "ab")]
    public void Sanitize_LineBreaks_StripsThem(string value, string expected)
    {
        Assert.Equal(expected, EmailTemplates.Sanitize(value));
    }

    [Fact]
    public void Sanitize_InjectionPayload_ContainsNoLineBreak()
    {
        string sanitized = EmailTemplates.Sanitize("Subject\r\nBcc: attacker@example.test");

        Assert.DoesNotContain("\r", sanitized);
        Assert.DoesNotContain("\n", sanitized);
    }

    [Theory]
    [InlineData("a\0b", "ab")]
    [InlineData("a\vb", "ab")]
    [InlineData("a\fb", "ab")]
    public void Sanitize_OtherControlCharacters_StripsThem(string value, string expected)
    {
        Assert.Equal(expected, EmailTemplates.Sanitize(value));
    }

    [Fact]
    public void Sanitize_EscapeAndDelete_StripsThem()
    {
        Assert.Equal("ab", EmailTemplates.Sanitize("a" + (char)0x1B + "b"));
        Assert.Equal("ab", EmailTemplates.Sanitize("a" + (char)0x7F + "b"));
    }

    [Theory]
    [InlineData("a\tb")]
    [InlineData("Café ☕ naïve")]
    public void Sanitize_TabAndOrdinaryText_ReturnedUnchanged(string value)
    {
        Assert.Equal(value, EmailTemplates.Sanitize(value));
    }

    [Fact]
    public void Sanitize_Null_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, EmailTemplates.Sanitize(null!));
    }

    [Fact]
    public void Sanitize_Empty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, EmailTemplates.Sanitize(string.Empty));
    }
}
