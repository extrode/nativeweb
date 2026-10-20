using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NativeWeb.SignIn;
using NativeWeb.SignIn.Testing;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class TestSessionTests
{
    [Fact]
    public async Task MintAsync_ProducesASessionTheHandlerAccepts()
    {
        using ServiceProvider provider = Host();

        string cookieValue = await TestSession.MintAsync(provider, "subject-1");
        AuthenticateResult result = await Authenticate(provider, cookieValue);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "subject-1",
            result.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    }

    [Fact]
    public async Task MintExpiredAsync_ProducesASessionTheHandlerRefuses()
    {
        using ServiceProvider provider = Host();

        string cookieValue = await TestSession.MintExpiredAsync(provider, "subject-1");

        Assert.True((await Authenticate(provider, cookieValue)).None);
    }

    [Fact]
    public async Task MintExpiredAsync_APositiveLapseIsRequired() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => TestSession.MintExpiredAsync(Host(), "subject-1", TimeSpan.Zero));

    [Fact]
    public async Task Tamper_ProducesASessionTheHandlerRefuses()
    {
        using ServiceProvider provider = Host();

        string cookieValue = await TestSession.MintAsync(provider, "subject-1");

        Assert.True((await Authenticate(provider, TestSession.Tamper(cookieValue))).None);
    }

    [Fact]
    public async Task Tamper_ChangesTheValueWhicheverCharacterItStartsWith()
    {
        using ServiceProvider provider = Host();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            string cookieValue = await TestSession.MintAsync(provider, "subject-1");
            Assert.NotEqual(cookieValue, TestSession.Tamper(cookieValue));
        }
    }

    [Fact]
    public async Task CookieHeader_NamesTheConfiguredCookie()
    {
        using ServiceProvider provider = Host();

        string cookieValue = await TestSession.MintAsync(provider, "subject-1");

        Assert.Equal($"__Host-signin={cookieValue}", TestSession.CookieHeader(provider, cookieValue));
    }

    [Fact]
    public async Task MintAsync_TwoSubjects_AreToldApart()
    {
        using ServiceProvider provider = Host();

        string first = await TestSession.MintAsync(provider, "subject-1");
        string second = await TestSession.MintAsync(provider, "subject-2");

        Assert.NotEqual(first, second);
        Assert.Equal(
            "subject-2",
            (await Authenticate(provider, second)).Principal!
                .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    }

    [Fact]
    public void TheLibrary_ExposesNoPublicMintingApi()
    {
        List<string> minting =
        [
            .. typeof(SessionManager).Assembly.GetExportedTypes()
                .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                .Where(m => m.Name.Contains("Mint", StringComparison.OrdinalIgnoreCase))
                .Select(m => $"{m.DeclaringType!.Name}.{m.Name}"),
        ];

        Assert.Empty(minting);
    }

    private static async Task<AuthenticateResult> Authenticate(
        IServiceProvider services, string cookieValue)
    {
        using IServiceScope scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Cookie = TestSession.CookieHeader(services, cookieValue);

        var handler = new SessionAuthenticationHandler(
            services.GetRequiredService<IOptionsMonitor<AuthenticationSchemeOptions>>(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            scope.ServiceProvider);

        await handler.InitializeAsync(
            new AuthenticationScheme(
                SessionAuthenticationHandler.SchemeName,
                null,
                typeof(SessionAuthenticationHandler)),
            context);

        return await handler.AuthenticateAsync();
    }

    private static ServiceProvider Host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(options =>
        {
            options.AuthorizationEndpoint = "https://provider.test/authorize";
            options.TokenEndpoint = "https://provider.test/token";
            options.UserInfoEndpoint = "https://provider.test/userinfo";
            options.ClientId = "client-id";
            options.ClientSecret = "client-secret";
            options.RedirectUri = "https://app.test/signin/callback";
            options.StoreEncryptionKey = RandomNumberGenerator.GetBytes(32);
        });

        return services.BuildServiceProvider();
    }
}

public sealed class InMemoryLoginFlowStoreConformanceTests : LoginFlowStoreConformance
{
    protected override ILoginFlowStore CreateStore(TimeProvider clock) => new InMemoryLoginFlowStore(clock);
}

public sealed class InMemorySessionStoreConformanceTests : SessionStoreConformance
{
    protected override ISessionStore CreateStore(TimeProvider clock) => new InMemorySessionStore(clock);
}
