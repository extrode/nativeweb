using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class AddSignInTests
{
    private const string Other = "OtherProvider";

    private static readonly ProviderTokens Tokens = new("access-1", "refresh-1", 1800);

    [Fact]
    public void AddSignIn_MisconfiguredOptions_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddSignIn(options =>
            {
                Configure(options);
                options.CookieName = "session";
            }));
    }

    [Fact]
    public void AddSignIn_RegistersTheOptionsUnderTheSchemeKey()
    {
        using ServiceProvider provider = Built();

        Assert.NotNull(
            provider.GetKeyedService<SignInOptions>(SessionAuthenticationHandler.SchemeName));
    }

    [Fact]
    public void AddSignIn_RegistersNothingUnkeyed()
    {
        using ServiceProvider provider = Built();

        Assert.Null(provider.GetService<SignInOptions>());
        Assert.Null(provider.GetService<SessionManager>());
    }

    [Fact]
    public void AddSignIn_RegistersTheClientStoresAndManager()
    {
        using ServiceProvider provider = Built();
        using IServiceScope scope = provider.CreateScope();
        string scheme = SessionAuthenticationHandler.SchemeName;

        Assert.NotNull(scope.ServiceProvider.GetKeyedService<OAuthClient>(scheme));
        Assert.NotNull(scope.ServiceProvider.GetKeyedService<ILoginFlowStore>(scheme));
        Assert.NotNull(scope.ServiceProvider.GetKeyedService<ISessionStore>(scheme));
        Assert.NotNull(scope.ServiceProvider.GetKeyedService<SessionManager>(scheme));
        Assert.NotNull(scope.ServiceProvider.GetKeyedService<IntrospectionCache>(scheme));
    }

    [Fact]
    public void AddSignIn_DefaultStoresOutliveTheRequestThatCreatedThem()
    {
        using ServiceProvider provider = Built();
        string scheme = SessionAuthenticationHandler.SchemeName;

        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredKeyedService<ISessionStore>(scheme),
            second.ServiceProvider.GetRequiredKeyedService<ISessionStore>(scheme));
        Assert.Same(
            first.ServiceProvider.GetRequiredKeyedService<ILoginFlowStore>(scheme),
            second.ServiceProvider.GetRequiredKeyedService<ILoginFlowStore>(scheme));
    }

    [Fact]
    public void AddSignIn_RegistersAntiforgeryAndAuthorization()
    {
        using ServiceProvider provider = Built();

        Assert.NotNull(provider.GetService<IAntiforgery>());
        Assert.NotNull(provider.GetService<IAuthorizationService>());
    }

    [Fact]
    public void AddSignIn_KeepsAConsumersOwnTimeProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var clock = new FixedClock();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSignIn(Configure);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddSignIn_RegistersTheStartupCheckBeforeTheSweeper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        List<IHostedService> resolved = [.. provider.GetServices<IHostedService>()];

        Assert.InRange(
            resolved.FindIndex(static s => s is SignInStartupCheck),
            0,
            resolved.FindIndex(static s => s is LoginFlowSweeper) - 1);
    }

    [Fact]
    public async Task StartupCheck_WithEveryServiceRegistered_Passes()
    {
        using ServiceProvider provider = Built();

        await provider.GetServices<IHostedService>().OfType<SignInStartupCheck>().Single()
            .StartAsync(default);
    }

    [Fact]
    public void AddSignIn_MakesTheFirstSchemeTheDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(Configure);
        services.AddSignIn(Other, Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        AuthenticationOptions authentication =
            provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(SessionAuthenticationHandler.SchemeName, authentication.DefaultScheme);
    }

    [Fact]
    public void AddSignIn_OneScheme_IsTheDefault()
    {
        using ServiceProvider provider = Built();

        Assert.Equal(
            SessionAuthenticationHandler.SchemeName,
            provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
    }

    [Fact]
    public void AddSignIn_TheSameSchemeTwice_IsRefused()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(Configure);

        Assert.Throws<InvalidOperationException>(() => services.AddSignIn(Configure));
    }

    [Fact]
    public void AddSignIn_TwoSchemes_KeepTheirOwnOptions()
    {
        using ServiceProvider provider = BuiltWithTwo();

        SignInOptions first = provider.GetRequiredKeyedService<SignInOptions>(
            SessionAuthenticationHandler.SchemeName);
        SignInOptions second = provider.GetRequiredKeyedService<SignInOptions>(Other);

        Assert.NotSame(first, second);
        Assert.Equal("__Host-signin", first.CookieName);
        Assert.Equal("__Host-other", second.CookieName);
    }

    [Fact]
    public async Task AddSignIn_TwoSchemes_ASessionForOneDoesNotAuthenticateAgainstTheOther()
    {
        using ServiceProvider provider = BuiltWithTwo();
        using IServiceScope scope = provider.CreateScope();

        SessionManager first = scope.ServiceProvider.GetRequiredKeyedService<SessionManager>(
            SessionAuthenticationHandler.SchemeName);
        SignInOptions firstOptions = provider.GetRequiredKeyedService<SignInOptions>(
            SessionAuthenticationHandler.SchemeName);

        string cookieValue = await first.MintAsync(
            new DefaultHttpContext(), new ProviderUser("subject-1", null, null), Tokens, default);

        AuthenticateResult underTheOther = await Authenticate(
            scope.ServiceProvider, Other, firstOptions.CookieName, cookieValue);
        AuthenticateResult underItsOwn = await Authenticate(
            scope.ServiceProvider,
            SessionAuthenticationHandler.SchemeName,
            firstOptions.CookieName,
            cookieValue);

        Assert.True(underItsOwn.Succeeded);
        Assert.True(underTheOther.None);
    }

    [Fact]
    public async Task AddSignIn_TwoSchemes_HaveTheirOwnSessionStores()
    {
        using ServiceProvider provider = BuiltWithTwo();
        using IServiceScope scope = provider.CreateScope();

        await scope.ServiceProvider
            .GetRequiredKeyedService<SessionManager>(SessionAuthenticationHandler.SchemeName)
            .MintAsync(new DefaultHttpContext(), new ProviderUser("subject-1", null, null), Tokens, default);

        Assert.Equal(
            1,
            scope.ServiceProvider
                .GetRequiredKeyedService<InMemorySessionStore>(SessionAuthenticationHandler.SchemeName)
                .Count);
        Assert.Equal(
            0,
            scope.ServiceProvider.GetRequiredKeyedService<InMemorySessionStore>(Other).Count);
    }

    private static async Task<AuthenticateResult> Authenticate(
        IServiceProvider services, string scheme, string cookieName, string cookieValue)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Cookie = $"{cookieName}={cookieValue}";

        var handler = new SessionAuthenticationHandler(
            services.GetRequiredService<IOptionsMonitor<AuthenticationSchemeOptions>>(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            services);

        await handler.InitializeAsync(
            new AuthenticationScheme(scheme, null, typeof(SessionAuthenticationHandler)),
            context);

        return await handler.AuthenticateAsync();
    }

    private static ServiceProvider Built()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(Configure);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuiltWithTwo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(Configure);
        services.AddSignIn(Other, options =>
        {
            Configure(options);
            options.CookieName = "__Host-other";
            options.AuthorizationEndpoint = "https://other.test/authorize";
            options.TokenEndpoint = "https://other.test/token";
            options.UserInfoEndpoint = "https://other.test/userinfo";
        });

        return services.BuildServiceProvider();
    }

    private static void Configure(SignInOptions options)
    {
        options.AuthorizationEndpoint = "https://provider.test/authorize";
        options.TokenEndpoint = "https://provider.test/token";
        options.UserInfoEndpoint = "https://provider.test/userinfo";
        options.ClientId = "client-id";
        options.ClientSecret = "client-secret";
        options.RedirectUri = "https://app.test/signin/callback";
        options.StoreEncryptionKey =
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
