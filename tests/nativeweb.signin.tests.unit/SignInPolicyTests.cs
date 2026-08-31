using System.Net;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SignInPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetLogin_PolicyRefuses_RedirectsToTheRefusedCode()
    {
        var flows = new CountingFlowStore();

        DefaultHttpContext context = await Login(flows, Refuse);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/signin-failed?error=refused", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task GetLogin_PolicyRefuses_RecordsNoFlow()
    {
        var flows = new CountingFlowStore();

        await Login(flows, Refuse);

        Assert.Equal(0, flows.Starts);
    }

    [Fact]
    public async Task GetLogin_PolicyRefuses_MakesNoProviderCall()
    {
        var flows = new CountingFlowStore();

        DefaultHttpContext context = await Login(flows, Refuse);

        Assert.DoesNotContain(
            "provider.test",
            context.Response.Headers.Location.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLogin_PolicyThrows_FailsClosed()
    {
        var flows = new CountingFlowStore();

        DefaultHttpContext context = await Login(
            flows, (_, _) => throw new InvalidOperationException("a broken policy"));

        Assert.Equal("/signin-failed?error=refused", context.Response.Headers.Location.ToString());
        Assert.Equal(0, flows.Starts);
    }

    [Fact]
    public async Task GetLogin_ClientGoesAway_IsNotSwallowedAsARefusal()
    {
        var flows = new CountingFlowStore();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        DefaultHttpContext context = new DefaultHttpContext().Executable();
        context.RequestAborted = aborted.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Run(
                context,
                flows,
                (_, ct) => throw new OperationCanceledException(ct)));
    }

    [Fact]
    public async Task GetLogin_TwoRefusedCallers_AreAnsweredIdentically()
    {
        DefaultHttpContext known = await Login(
            new CountingFlowStore(), Refuse, subject: "has-a-local-account");
        DefaultHttpContext stranger = await Login(
            new CountingFlowStore(), Refuse, subject: "has-no-local-account");

        Assert.Equal(known.Response.StatusCode, stranger.Response.StatusCode);
        Assert.Equal(
            known.Response.Headers.Location.ToString(),
            stranger.Response.Headers.Location.ToString());
        Assert.Equal(
            known.Response.Headers.Select(h => h.Key).Order(StringComparer.Ordinal),
            stranger.Response.Headers.Select(h => h.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetLogin_TwoRefusedCallers_AreAnsweredWithIdenticalHeaderValues()
    {
        DefaultHttpContext known = await Login(
            new CountingFlowStore(), Refuse, subject: "has-a-local-account");
        DefaultHttpContext stranger = await Login(
            new CountingFlowStore(), Refuse, subject: "has-no-local-account");

        Assert.Equal(
            known.Response.Headers.Select(h => $"{h.Key}: {h.Value}").Order(StringComparer.Ordinal),
            stranger.Response.Headers.Select(h => $"{h.Key}: {h.Value}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetLogin_PolicyAccepts_BeginsTheFlow()
    {
        var flows = new CountingFlowStore();

        DefaultHttpContext context = await Login(flows, (_, _) => ValueTask.FromResult(true));

        Assert.Equal(1, flows.Starts);
        Assert.StartsWith(
            "https://provider.test/authorize?",
            context.Response.Headers.Location.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLogin_NoPolicy_BehavesAsIfTheSeamWereNotThere()
    {
        var withNone = new CountingFlowStore();
        var withAccepting = new CountingFlowStore();

        DefaultHttpContext none = await Login(withNone, policy: null);
        DefaultHttpContext accepting = await Login(
            withAccepting, (_, _) => ValueTask.FromResult(true));

        Assert.Equal(1, withNone.Starts);
        Assert.Equal(accepting.Response.StatusCode, none.Response.StatusCode);
        Assert.Equal(
            accepting.Response.Headers.Select(h => h.Key).Order(StringComparer.Ordinal),
            none.Response.Headers.Select(h => h.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Callback_Resolver_MintsTheSessionAgainstTheLocalKey()
    {
        Fixture fixture = await Signed(
            (_, subject, _, _) => ValueTask.FromResult<string?>("local-" + subject.Value));

        SessionState read = await fixture.Read();

        Assert.Equal("local-subject-1", read.Subject);
    }

    [Fact]
    public async Task Callback_Resolver_SeesTheProviderSubjectAndTheContactSeparately()
    {
        string? seenSubject = null;
        string? seenEmail = null;

        await Signed((_, subject, contact, _) =>
        {
            seenSubject = subject.Value;
            seenEmail = contact.Email;
            return ValueTask.FromResult<string?>("local-1");
        });

        Assert.Equal("subject-1", seenSubject);
        Assert.Equal("a@example.test", seenEmail);
    }

    [Fact]
    public async Task Callback_ResolverRefuses_MintsNoSession()
    {
        Fixture fixture = await Signed((_, _, _, _) => ValueTask.FromResult<string?>(null));

        Assert.Equal(0, fixture.Sessions.Count);
        Assert.False(fixture.Context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task Callback_ResolverRefuses_ReportsTheRefusedCode()
    {
        Fixture fixture = await Signed((_, _, _, _) => ValueTask.FromResult<string?>(null));

        Assert.Equal(
            "/signin-failed?error=refused",
            fixture.Context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_NoResolver_KeepsTheProvidersOwnSubject()
    {
        Fixture fixture = await Signed(resolver: null);

        SessionState read = await fixture.Read();

        Assert.Equal("subject-1", read.Subject);
    }

    [Fact]
    public void WithSignInPolicy_ASecondTime_IsRefused()
    {
        SignInBuilder builder = Registered();
        builder.WithSignInPolicy(Refuse);

        Assert.Throws<InvalidOperationException>(() => builder.WithSignInPolicy(Refuse));
    }

    [Fact]
    public void OnSignedIn_ASecondTime_IsRefused()
    {
        SignInBuilder builder = Registered();
        builder.OnSignedIn((_, subject, _, _) => ValueTask.FromResult<string?>(subject.Value));

        Assert.Throws<InvalidOperationException>(
            () => builder.OnSignedIn((_, _, _, _) => ValueTask.FromResult<string?>("other")));
    }

    [Fact]
    public void WithSignInPolicy_Null_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => Registered().WithSignInPolicy(null!));

    [Fact]
    public void OnSignedIn_Null_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => Registered().OnSignedIn(null!));

    [Fact]
    public void WithSessionStore_ReplacesTheDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(ConfigureOptions).WithSessionStore<NoStore>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<NoStore>(
            scope.ServiceProvider.GetRequiredKeyedService<ISessionStore>(
                SessionAuthenticationHandler.SchemeName));
    }

    [Fact]
    public void WithFlowStore_ReplacesTheDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignIn(ConfigureOptions).WithFlowStore<NoFlows>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<NoFlows>(
            scope.ServiceProvider.GetRequiredKeyedService<ILoginFlowStore>(
                SessionAuthenticationHandler.SchemeName));
    }

    private static ValueTask<bool> Refuse(HttpContext http, CancellationToken ct) =>
        ValueTask.FromResult(false);

    private static SignInBuilder Registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return services.AddSignIn(ConfigureOptions);
    }

    private static void ConfigureOptions(SignInOptions options)
    {
        SignInOptions source = Options();
        options.AuthorizationEndpoint = source.AuthorizationEndpoint;
        options.TokenEndpoint = source.TokenEndpoint;
        options.UserInfoEndpoint = source.UserInfoEndpoint;
        options.ClientId = source.ClientId;
        options.ClientSecret = source.ClientSecret;
        options.RedirectUri = source.RedirectUri;
        options.StoreEncryptionKey = source.StoreEncryptionKey;
    }

    private static async Task<DefaultHttpContext> Login(
        ILoginFlowStore flows, SignInPolicy? policy, string? subject = null)
    {
        DefaultHttpContext context = new DefaultHttpContext().Executable();

        if (subject is not null)
        {
            context.Request.QueryString = new QueryString("?returnTo=/" + subject);
        }

        await Run(context, flows, policy);

        return context;
    }

    private static async Task Run(
        DefaultHttpContext context, ILoginFlowStore flows, SignInPolicy? policy)
    {
        SignInOptions options = Options();
        var client = new OAuthClient(
            new HttpClient(new RefusingHandler()), options, NullLogger<OAuthClient>.Instance);

        IResult result = await SignInEndpoints.GetLoginAsync(
            context,
            client,
            flows,
            options,
            new FixedClock(Now),
            NullLoggerFactory.Instance,
            policy);

        await result.ExecuteAsync(context);
    }

    private static async Task<Fixture> Signed(SignInResolver? resolver)
    {
        SignInOptions options = Options();
        var clock = new FixedClock(Now);
        var flows = new InMemoryLoginFlowStore(clock);
        var client = new OAuthClient(
            new HttpClient(new AnsweringProvider()), options, NullLogger<OAuthClient>.Instance);
        var sessions = new InMemorySessionStore(clock);
        var manager = new SessionManager(
            sessions, client, options, clock, NullLogger<SessionManager>.Instance);

        DefaultHttpContext begin = new DefaultHttpContext().Executable();
        IResult redirect = await SignInEndpoints.GetLoginAsync(
            begin, client, flows, options, clock, NullLoggerFactory.Instance);
        await redirect.ExecuteAsync(begin);

        string state = System.Web.HttpUtility
            .ParseQueryString(new Uri(begin.Response.Headers.Location.ToString()).Query)["state"]!;

        DefaultHttpContext callback = new DefaultHttpContext().Executable();
        callback.Request.QueryString = new QueryString($"?code=code-1&state={state}");

        IResult result = await SignInEndpoints.GetCallbackAsync(
            callback, client, flows, manager, options, NullLoggerFactory.Instance, resolver);
        await result.ExecuteAsync(callback);

        return new Fixture(manager, sessions, options, callback);
    }

    private sealed record Fixture(
        SessionManager Manager,
        InMemorySessionStore Sessions,
        SignInOptions Options,
        DefaultHttpContext Context)
    {
        internal Task<SessionState> Read()
        {
            string cookie = Assert.Single(Context.Response.Headers.SetCookie!)!;
            string value = cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..]
                .Split(';')[0];

            var next = new DefaultHttpContext();
            next.Request.Headers.Cookie = $"{Options.CookieName}={value}";

            return Manager.ReadAsync(next, default);
        }
    }

    private sealed class CountingFlowStore : ILoginFlowStore
    {
        public int Starts { get; private set; }

        public Task StartAsync(
            string state, string verifier, string returnTo, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Starts++;
            return Task.CompletedTask;
        }

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct) =>
            Task.FromResult(FlowTakeResult.Unknown());

        public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoStore : ISessionStore
    {
        public Task CreateAsync(string keyHash, SessionRecord record, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<SessionRecord?> GetAsync(string keyHash, CancellationToken ct) =>
            Task.FromResult<SessionRecord?>(null);

        public Task<SessionReplace> ReplaceIfMatchAsync(
            string keyHash,
            string expectedRefreshTokenHash,
            SessionRecord replacement,
            CancellationToken ct) =>
            Task.FromResult(SessionReplace.Gone);

        public Task RemoveAsync(string keyHash, CancellationToken ct) => Task.CompletedTask;

        public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoFlows : ILoginFlowStore
    {
        public Task StartAsync(
            string state, string verifier, string returnTo, DateTimeOffset expiresAt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct) =>
            Task.FromResult(FlowTakeResult.Unknown());

        public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A refused sign-in must not call the provider.");
    }

    private sealed class AnsweringProvider : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(
                request.RequestUri!.AbsoluteUri.EndsWith("/token", StringComparison.Ordinal)
                    ? Json(
                        HttpStatusCode.OK,
                        "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\","
                        + "\"expires_in\":1800}")
                    : Json(
                        HttpStatusCode.OK,
                        "{\"sub\":\"subject-1\",\"email\":\"a@example.test\"}"));
    }
}
