using System.Reflection;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Http;

using NativeWeb.Security;
using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SignInOptionsTests
{
    [Fact]
    public void Validate_FullyConfigured_Passes()
    {
        Valid().Validate();
    }

    [Fact]
    public void Validate_NothingConfigured_Passes()
    {
        var options = new SignInOptions { AllowEphemeralStoreEncryptionKey = true };

        options.Validate();

        Assert.False(options.IsConfigured);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("__Secure-signin")]
    [InlineData("host-signin")]
    public void Validate_CookieNameWithoutHostPrefix_Throws(string name)
    {
        SignInOptions options = Valid();
        options.CookieName = name;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.CookieName), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NoKeyWithoutTheEphemeralOptIn_Throws()
    {
        SignInOptions options = Valid();
        options.StoreEncryptionKey = null;
        options.AllowEphemeralStoreEncryptionKey = false;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.StoreEncryptionKey), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NoKeyWithTheEphemeralOptIn_MintsOne()
    {
        SignInOptions options = Valid();
        options.StoreEncryptionKey = null;
        options.AllowEphemeralStoreEncryptionKey = true;

        options.Validate();

        Assert.NotNull(options.StoreEncryptionKey);
        Assert.Equal(StoreDataProtector.KeyBytes, options.StoreEncryptionKey!.Length);
        var protector = new StoreDataProtector(options.StoreEncryptionKey);
        Assert.Equal("token", protector.Unprotect(protector.Protect("token")));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(64)]
    public void Validate_KeyOfTheWrongLength_Throws(int bytes)
    {
        SignInOptions options = Valid();
        options.StoreEncryptionKey = RandomNumberGenerator.GetBytes(bytes);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.StoreEncryptionKey), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)7)]
    [InlineData((byte)255)]
    public void Validate_SingleRepeatedByteKey_Throws(byte fill)
    {
        SignInOptions options = Valid();
        byte[] key = new byte[StoreDataProtector.KeyBytes];
        Array.Fill(key, fill);
        options.StoreEncryptionKey = key;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("repeated byte", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.test/")]
    [InlineData("//evil.test")]
    [InlineData("/\\evil.test")]
    [InlineData("")]
    public void Validate_OffSiteDefaultReturnPath_Throws(string path)
    {
        SignInOptions options = Valid();
        options.DefaultReturnPath = path;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.DefaultReturnPath), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonRelativeLoginPath_Throws()
    {
        SignInOptions options = Valid();
        options.LoginPath = "https://evil.test/signin";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.LoginPath), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonRelativeCallbackPath_Throws()
    {
        SignInOptions options = Valid();
        options.CallbackPath = "//evil.test/cb";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.CallbackPath), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonRelativeLogoutPath_Throws()
    {
        SignInOptions options = Valid();
        options.LogoutPath = "https://evil.test/signout";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.LogoutPath), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonRelativeSignedOutPath_Throws()
    {
        SignInOptions options = Valid();
        options.SignedOutPath = "https://evil.test/";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.SignedOutPath), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_UnparseableAuthorizationEndpoint_Throws()
    {
        SignInOptions options = Valid();
        options.AuthorizationEndpoint = "not a url";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.AuthorizationEndpoint), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonHttpTokenEndpoint_Throws()
    {
        SignInOptions options = Valid();
        options.TokenEndpoint = "ftp://provider.test/token";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.TokenEndpoint), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_UnparseableUserInfoEndpoint_Throws()
    {
        SignInOptions options = Valid();
        options.UserInfoEndpoint = "/relative/userinfo";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.UserInfoEndpoint), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_UnparseableIntrospectionEndpoint_Throws()
    {
        SignInOptions options = Valid();
        options.IntrospectionEndpoint = "not a url";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.IntrospectionEndpoint), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AbsentIntrospectionEndpoint_Passes()
    {
        SignInOptions options = Valid();
        options.IntrospectionEndpoint = "";

        options.Validate();
    }

    [Fact]
    public void Validate_RedirectUriPathDisagreesWithCallbackPath_Throws()
    {
        SignInOptions options = Valid();
        options.RedirectUri = "https://app.test/oauth/return";
        options.CallbackPath = "/signin/callback";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("/oauth/return", error.Message, StringComparison.Ordinal);
        Assert.Contains("/signin/callback", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RedirectUriNotAbsolute_Throws()
    {
        SignInOptions options = Valid();
        options.RedirectUri = "/signin/callback";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.RedirectUri), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PartlyConfigured_ThrowsNamingWhatIsMissing()
    {
        var options = new SignInOptions
        {
            AuthorizationEndpoint = "https://provider.test/authorize",
            AllowEphemeralStoreEncryptionKey = true,
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.TokenEndpoint), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SignInOptions.ClientId), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SignInOptions.RedirectUri), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SignInOptions.ClientSecret), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PublicClientWithoutASecret_Passes()
    {
        SignInOptions options = Valid();
        options.ClientAuthentication = ClientAuthenticationMode.None;
        options.ClientSecret = "";

        options.Validate();

        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Validate_ConfidentialClientWithoutASecret_Throws()
    {
        SignInOptions options = Valid();
        options.ClientAuthentication = ClientAuthenticationMode.ClientSecretPost;
        options.ClientSecret = "";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.ClientSecret), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveFlowLifetime_Throws(int minutes)
    {
        SignInOptions options = Valid();
        options.FlowLifetime = TimeSpan.FromMinutes(minutes);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.FlowLifetime), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveSessionLifetime_Throws(int days)
    {
        SignInOptions options = Valid();
        options.SessionLifetime = TimeSpan.FromDays(days);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.SessionLifetime), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NegativeRefreshSkew_Throws()
    {
        SignInOptions options = Valid();
        options.RefreshSkew = TimeSpan.FromSeconds(-1);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.RefreshSkew), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NegativeIntrospectionCacheLifetime_Throws()
    {
        SignInOptions options = Valid();
        options.IntrospectionCacheLifetime = TimeSpan.FromSeconds(-1);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SignInOptions.IntrospectionCacheLifetime), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ZeroIntrospectionCacheLifetime_Passes()
    {
        SignInOptions options = Valid();
        options.IntrospectionCacheLifetime = TimeSpan.Zero;

        options.Validate();
    }

    [Fact]
    public void CookieAttributes_AreFixed()
    {
        Assert.True(SignInOptions.CookieHttpOnly);
        Assert.True(SignInOptions.CookieSecure);
        Assert.Equal(SameSiteMode.Lax, SignInOptions.CookieSameSite);
    }

    [Fact]
    public void CookieAttributes_HaveNoSetter()
    {
        foreach (string name in new[] { "CookieHttpOnly", "CookieSecure", "CookieSameSite" })
        {
            PropertyInfo? property = typeof(SignInOptions).GetProperty(name);

            Assert.NotNull(property);
            Assert.Null(property!.SetMethod);
        }
    }

    [Fact]
    public void Options_ExposeNoWritableCookieSecurityAttribute()
    {
        string[] forbidden = ["Secure", "HttpOnly", "SameSite", "Domain"];

        foreach (PropertyInfo property in typeof(SignInOptions).GetProperties())
        {
            Assert.DoesNotContain(property.Name, forbidden);
        }
    }

    [Fact]
    public void Defaults_NameNoProviderAndStayInsideTheApplication()
    {
        var options = new SignInOptions();

        Assert.Equal("", options.AuthorizationEndpoint);
        Assert.Equal("", options.TokenEndpoint);
        Assert.Equal("", options.UserInfoEndpoint);
        Assert.Equal("", options.IntrospectionEndpoint);
        Assert.Equal("/signin", options.LoginPath);
        Assert.Equal("/signin/callback", options.CallbackPath);
        Assert.Equal("/signout", options.LogoutPath);
        Assert.Equal("/", options.SignedOutPath);
        Assert.Equal("/", options.DefaultReturnPath);
        Assert.Equal("__Host-signin", options.CookieName);
        Assert.True(options.RetainTokens);
        Assert.Equal(["openid", "profile", "email"], options.Scopes);
    }

    [Fact]
    public void IsConfigured_MissingAnyRequiredSetting_IsFalse()
    {
        Assert.False(WithoutValue(o => o.AuthorizationEndpoint = "").IsConfigured);
        Assert.False(WithoutValue(o => o.TokenEndpoint = "").IsConfigured);
        Assert.False(WithoutValue(o => o.UserInfoEndpoint = "").IsConfigured);
        Assert.False(WithoutValue(o => o.ClientId = "").IsConfigured);
        Assert.False(WithoutValue(o => o.RedirectUri = "").IsConfigured);
        Assert.False(WithoutValue(o => o.ClientSecret = "").IsConfigured);
        Assert.True(Valid().IsConfigured);
    }

    private static SignInOptions WithoutValue(Action<SignInOptions> clear)
    {
        SignInOptions options = Valid();
        clear(options);
        return options;
    }

    private static SignInOptions Valid() =>
        new()
        {
            AuthorizationEndpoint = "https://provider.test/authorize",
            TokenEndpoint = "https://provider.test/token",
            UserInfoEndpoint = "https://provider.test/userinfo",
            IntrospectionEndpoint = "https://provider.test/introspect",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://app.test/signin/callback",
            StoreEncryptionKey = RandomNumberGenerator.GetBytes(StoreDataProtector.KeyBytes),
        };
}
