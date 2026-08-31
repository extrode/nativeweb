using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Security.Tests.Unit;

/// <summary>
/// Display masking for contact destinations. Every test asserts both the shape of
/// the output and that the characters each mask is supposed to hide are absent.
/// </summary>
public sealed class MaskingTests
{
    [Fact]
    public void Email_DocCommentExample_KeepsFirstCharsAndTheTld()
    {
        string masked = Masking.Email("alice@example.test");

        Assert.Equal("a•••@e•••.test", masked);
        Assert.DoesNotContain("lice", masked);
        Assert.DoesNotContain("xample", masked);
    }

    [Fact]
    public void Email_SingleLabelDomain_LeavesTheOnlyLabelIntact()
    {
        string masked = Masking.Email("alice@example");

        Assert.Equal("a•••@example", masked);
        Assert.DoesNotContain("lice", masked);
    }

    [Fact]
    public void Email_MultiLabelDomain_MasksEveryLabelButTheLast()
    {
        string masked = Masking.Email("user@a.b.example.co.uk");

        Assert.Equal("u•••@a•••.b•••.e•••.c•••.uk", masked);
        Assert.DoesNotContain("ser", masked);
        Assert.DoesNotContain("xample", masked);
    }

    [Fact]
    public void Email_OneCharacterLocalPart_HidesTheDomainBody()
    {
        string masked = Masking.Email("a@example.test");

        Assert.Equal("a•••@e•••.test", masked);
        Assert.DoesNotContain("xample", masked);
    }

    [Fact]
    public void Email_NoAtSign_DegradesToFullyMaskedToken()
    {
        string masked = Masking.Email("aliceexample.test");

        Assert.Equal("a•••", masked);
        Assert.DoesNotContain("example", masked);
    }

    [Fact]
    public void Email_LeadingAtSign_DegradesToFullyMaskedToken()
    {
        string masked = Masking.Email("@example.test");

        Assert.Equal("@•••", masked);
        Assert.DoesNotContain("example", masked);
    }

    [Fact]
    public void Email_TrailingAtSign_DegradesToFullyMaskedToken()
    {
        string masked = Masking.Email("alice@");

        Assert.Equal("a•••", masked);
        Assert.DoesNotContain("lice", masked);
    }

    [Fact]
    public void Email_TwoAtSigns_MasksTheLocalPartOnly()
    {
        string masked = Masking.Email("alice@example@test");

        Assert.Equal("a•••@example@test", masked);
        Assert.DoesNotContain("lice", masked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Email_NullOrEmpty_Throws(string? email)
    {
        Assert.ThrowsAny<ArgumentException>(() => Masking.Email(email!));
    }

    [Fact]
    public void Phone_DocCommentExample_ShowsCountryCodeAndLastFourOnly()
    {
        string masked = Masking.Phone("+15550000002");

        Assert.Equal("+1 (•••) •••-0002", masked);
        Assert.DoesNotContain("555", masked);
    }

    [Fact]
    public void Phone_TenDigits_AssumesDefaultCountryCodeOne()
    {
        string masked = Masking.Phone("5551234567");

        Assert.Equal("+1 (•••) •••-4567", masked);
        Assert.DoesNotContain("555", masked);
        Assert.DoesNotContain("123", masked);
    }

    [Fact]
    public void Phone_ElevenDigits_KeepsTheLeadingCountryDigit()
    {
        string masked = Masking.Phone("15551234567");

        Assert.Equal("+1 (•••) •••-4567", masked);
        Assert.DoesNotContain("555", masked);
    }

    [Fact]
    public void Phone_PunctuationAndSpaces_MasksAllButTheLastFourDigits()
    {
        string masked = Masking.Phone("(555) 123-4567");

        Assert.Equal("+1 (•••) •••-4567", masked);
        Assert.DoesNotContain("555", masked);
        Assert.DoesNotContain("123", masked);
    }

    [Fact]
    public void Phone_ForeignE164_KeepsTheMultiDigitCountryCode()
    {
        string masked = Masking.Phone("+447700900123");

        Assert.Equal("+44 (•••) •••-0123", masked);
        Assert.DoesNotContain("7700", masked);
    }

    [Theory]
    [InlineData("12", "+1 (•••) •••-12")]
    [InlineData("911", "+1 (•••) •••-911")]
    public void Phone_FewerThanFourDigits_ShowsEveryDigitItHas(string phone, string expected)
    {
        Assert.Equal(expected, Masking.Phone(phone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Phone_NullOrEmpty_Throws(string? phone)
    {
        Assert.ThrowsAny<ArgumentException>(() => Masking.Phone(phone!));
    }

    [Fact]
    public void IpAddress_Ipv4_DropsTheLastOctet()
    {
        string masked = Masking.IpAddress("203.0.113.42");

        Assert.Equal("203.0.113.x", masked);
        Assert.DoesNotContain("42", masked);
    }

    [Fact]
    public void IpAddress_Ipv6_DropsTheLastSegment()
    {
        string masked = Masking.IpAddress("2001:db8::abcd");

        Assert.Equal("2001:db8::x", masked);
        Assert.DoesNotContain("abcd", masked);
    }

    [Fact]
    public void IpAddress_NoSeparator_FullyMasked()
    {
        Assert.Equal("•••", Masking.IpAddress("intranet-host"));
    }

    [Fact]
    public void IpAddress_TrailingSeparator_FullyMasked()
    {
        Assert.Equal("•••", Masking.IpAddress("203.0.113."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IpAddress_NullOrEmpty_Throws(string? ip)
    {
        Assert.ThrowsAny<ArgumentException>(() => Masking.IpAddress(ip!));
    }
}
