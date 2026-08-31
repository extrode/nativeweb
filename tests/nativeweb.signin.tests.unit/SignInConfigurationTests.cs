using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

/// <summary>
/// R17 at the configuration seam: every endpoint, the client identity, the scopes and the
/// paths come from a section, and nothing else in the options is reachable from one.
/// </summary>
public sealed class SignInConfigurationTests
{
    private static IConfiguration Section(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>("SignIn:" + p.Key, p.Value)))
            .Build()
            .GetSection("SignIn");

    [Fact]
    public void Fill_AppliesEveryEndpointAndTheClient()
    {
        var options = new SignInOptions();

        SignInConfiguration.Fill(options, Section(
            ("AuthorizationEndpoint", "https://p.test/authorize"),
            ("TokenEndpoint", "https://p.test/token"),
            ("UserInfoEndpoint", "https://p.test/userinfo"),
            ("IntrospectionEndpoint", "https://p.test/introspect"),
            ("ClientId", "product"),
            ("ClientSecret", "shh"),
            ("RedirectUri", "https://app.test/signin/callback")));

        Assert.Equal("https://p.test/authorize", options.AuthorizationEndpoint);
        Assert.Equal("https://p.test/token", options.TokenEndpoint);
        Assert.Equal("https://p.test/userinfo", options.UserInfoEndpoint);
        Assert.Equal("https://p.test/introspect", options.IntrospectionEndpoint);
        Assert.Equal("product", options.ClientId);
        Assert.Equal("shh", options.ClientSecret);
        Assert.Equal("https://app.test/signin/callback", options.RedirectUri);
    }

    [Fact]
    public void Fill_AppliesThePaths()
    {
        var options = new SignInOptions();

        SignInConfiguration.Fill(options, Section(
            ("CookieName", "__Host-product"),
            ("LoginPath", "/enter"),
            ("CallbackPath", "/enter/back"),
            ("LogoutPath", "/leave"),
            ("SignedOutPath", "/bye"),
            ("DefaultReturnPath", "/home")));

        Assert.Equal("__Host-product", options.CookieName);
        Assert.Equal("/enter", options.LoginPath);
        Assert.Equal("/enter/back", options.CallbackPath);
        Assert.Equal("/leave", options.LogoutPath);
        Assert.Equal("/bye", options.SignedOutPath);
        Assert.Equal("/home", options.DefaultReturnPath);
    }

    [Fact]
    public void Fill_AnEmptySection_ChangesNothing()
    {
        var options = new SignInOptions();
        var untouched = new SignInOptions();

        SignInConfiguration.Fill(options, Section());

        Assert.Equal(untouched.CookieName, options.CookieName);
        Assert.Equal(untouched.LoginPath, options.LoginPath);
        Assert.Equal(untouched.SessionLifetime, options.SessionLifetime);
        Assert.Equal(untouched.ClientAuthentication, options.ClientAuthentication);
        Assert.Equal(untouched.RetainTokens, options.RetainTokens);
        Assert.Equal(untouched.Scopes, options.Scopes);
        Assert.Null(options.StoreEncryptionKey);
    }

    [Fact]
    public void Fill_AnEmptyValue_IsTreatedAsAbsent()
    {
        var options = new SignInOptions();

        SignInConfiguration.Fill(options, Section(("LoginPath", "")));

        Assert.Equal("/signin", options.LoginPath);
    }

    [Fact]
    public void Fill_ReadsTheLifetimesAsSeconds()
    {
        var options = new SignInOptions();

        SignInConfiguration.Fill(options, Section(
            ("FlowLifetimeSeconds", "60"),
            ("SessionLifetimeSeconds", "3600"),
            ("RefreshSkewSeconds", "30"),
            ("IntrospectionCacheLifetimeSeconds", "5")));

        Assert.Equal(TimeSpan.FromSeconds(60), options.FlowLifetime);
        Assert.Equal(TimeSpan.FromSeconds(3600), options.SessionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshSkew);
        Assert.Equal(TimeSpan.FromSeconds(5), options.IntrospectionCacheLifetime);
    }

    [Fact]
    public void Fill_ALifetimeThatIsNotANumber_Throws()
    {
        var options = new SignInOptions();

        FormatException thrown = Assert.Throws<FormatException>(
            () => SignInConfiguration.Fill(options, Section(("SessionLifetimeSeconds", "fourteen days"))));

        Assert.Contains("SignIn:SessionLifetimeSeconds", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_ReadsTheClientAuthenticationMode()
    {
        var options = new SignInOptions();

        SignInConfiguration.Fill(options, Section(("ClientAuthentication", "clientsecretpost")));

        Assert.Equal(ClientAuthenticationMode.ClientSecretPost, options.ClientAuthentication);
    }

    [Fact]
    public void Fill_AnUnknownClientAuthenticationMode_ThrowsAndNamesTheChoices()
    {
        var options = new SignInOptions();

        FormatException thrown = Assert.Throws<FormatException>(
            () => SignInConfiguration.Fill(options, Section(("ClientAuthentication", "mutual-tls"))));

        Assert.Contains("ClientSecretBasic", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_AFlagThatIsNotABoolean_Throws() =>
        Assert.Throws<FormatException>(
            () => SignInConfiguration.Fill(new SignInOptions(), Section(("RetainTokens", "yes"))));

    [Fact]
    public void Fill_ReadsTheFlags()
    {
        var options = new SignInOptions();

        SignInConfiguration.Fill(options, Section(
            ("RetainTokens", "false"),
            ("AllowEphemeralStoreEncryptionKey", "true")));

        Assert.False(options.RetainTokens);
        Assert.True(options.AllowEphemeralStoreEncryptionKey);
    }

    [Fact]
    public void Fill_ReadsTheStoreEncryptionKeyAsBase64()
    {
        var options = new SignInOptions();
        byte[] key = [1, 2, 3, 4];

        SignInConfiguration.Fill(options, Section(("StoreEncryptionKey", Convert.ToBase64String(key))));

        Assert.Equal(key, options.StoreEncryptionKey);
    }

    [Fact]
    public void Fill_AStoreEncryptionKeyThatIsNotBase64_Throws() =>
        Assert.Throws<FormatException>(
            () => SignInConfiguration.Fill(new SignInOptions(), Section(("StoreEncryptionKey", "not base64!"))));

    [Fact]
    public void Fill_ConfiguredScopes_ReplaceTheDefaultsRatherThanJoinThem()
    {
        var options = new SignInOptions();

        SignInConfiguration.Fill(options, Section(("Scopes:0", "openid"), ("Scopes:1", "offline_access")));

        Assert.Equal(["openid", "offline_access"], options.Scopes);
    }

    [Fact]
    public void AddSignIn_FromASection_RegistersTheSchemesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSignIn(Section(
            ("AuthorizationEndpoint", "https://p.test/authorize"),
            ("TokenEndpoint", "https://p.test/token"),
            ("UserInfoEndpoint", "https://p.test/userinfo"),
            ("IntrospectionEndpoint", "https://p.test/introspect"),
            ("ClientId", "product"),
            ("ClientSecret", "shh"),
            ("RedirectUri", "https://app.test/signin/callback"),
            ("StoreEncryptionKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))));

        SignInOptions options = services.BuildServiceProvider()
            .GetRequiredKeyedService<SignInOptions>(SessionAuthenticationHandler.SchemeName);

        Assert.Equal("product", options.ClientId);
        Assert.Equal("https://p.test/token", options.TokenEndpoint);
    }

    [Fact]
    public void AddSignIn_FromANullSection_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddSignIn((IConfiguration)null!));
}
