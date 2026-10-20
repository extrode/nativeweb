using NativeWeb.Web;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

public sealed class AdminPassphraseTests
{
    [Fact]
    public void TheConfiguredSecretMatches()
    {
        var passphrase = new AdminPassphrase("correct horse battery staple");

        Assert.True(passphrase.IsConfigured);
        Assert.True(passphrase.Matches("correct horse battery staple"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("correct horse battery stapl")]
    [InlineData("correct horse battery staple ")]
    [InlineData("Correct horse battery staple")]
    [InlineData("wrong")]
    public void AnythingElseIsRefused(string? candidate)
    {
        var passphrase = new AdminPassphrase("correct horse battery staple");

        Assert.False(passphrase.Matches(candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnsetSecretRefusesEverythingIncludingEmpty(string? secret)
    {
        var passphrase = new AdminPassphrase(secret);

        Assert.False(passphrase.IsConfigured);
        Assert.False(passphrase.Matches(""));
        Assert.False(passphrase.Matches(null));
        Assert.False(passphrase.Matches("anything"));
    }

    [Theory]
    [InlineData("/admin/articles", "/admin/articles")]
    [InlineData("/", "/admin")]
    [InlineData("", "/admin")]
    [InlineData(null, "/admin")]
    [InlineData("//evil.example/x", "/admin")]
    [InlineData("/\\evil.example/x", "/admin")]
    [InlineData("https://evil.example/x", "/admin")]
    [InlineData("admin", "/admin")]
    [InlineData("/\t/evil.example/x", "/admin")]
    [InlineData("/admin\r\nLocation: https://evil.example", "/admin")]
    public void OnlyALocalPathIsReturnedTo(string? candidate, string expected)
    {
        Assert.Equal(expected, AdminSignIn.LocalPath(candidate));
    }

    [Fact]
    public void TheSignInLimitIsStricterThanTheGeneralPolicy()
    {
        var general = new NativeWebOptions
        {
            ApplicationName = "sample",
            ConfigurationSection = "Sample",
            TelemetryServiceName = "sample",
            DataProtectionKeyDirectory = "unused",
            UploadDirectory = "unused",
        };

        Assert.True(AdminSignIn.PermitLimit < general.GeneralPermitLimit);
    }
}
