using System.Net;
using System.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SignInRedirectTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetLogin_RedirectsToTheAuthorizationEndpoint()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));

        string location = await Login(flows);

        Assert.StartsWith("https://provider.test/authorize?", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLogin_CarriesTheAuthorizationCodeParameters()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));

        System.Collections.Specialized.NameValueCollection query = Query(await Login(flows));

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("https://app.test/signin/callback", query["redirect_uri"]);
        Assert.Equal("openid profile email", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
    }

    [Fact]
    public async Task GetLogin_CarriesAStateAndAChallenge()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));

        System.Collections.Specialized.NameValueCollection query = Query(await Login(flows));

        Assert.Equal(43, query["state"]!.Length);
        Assert.Equal(43, query["code_challenge"]!.Length);
        Assert.NotEqual(query["state"], query["code_challenge"]);
    }

    [Fact]
    public async Task GetLogin_NeverSendsTheVerifierToTheProvider()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));
        string location = await Login(flows);

        Assert.DoesNotContain("code_verifier", location, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLogin_TwoCalls_DifferInStateAndChallenge()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));

        System.Collections.Specialized.NameValueCollection first = Query(await Login(flows));
        System.Collections.Specialized.NameValueCollection second = Query(await Login(flows));

        Assert.NotEqual(first["state"], second["state"]);
        Assert.NotEqual(first["code_challenge"], second["code_challenge"]);
    }

    [Fact]
    public async Task GetLogin_RecordsTheFlowAgainstTheStateItSends()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));

        System.Collections.Specialized.NameValueCollection query = Query(await Login(flows));

        FlowTakeResult taken = await flows.TakeAsync(query["state"]!, default);
        Assert.Equal(FlowTake.Taken, taken.Outcome);
    }

    [Fact]
    public async Task GetLogin_RecordsTheVerifierThatMatchesTheChallengeItSent()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));

        System.Collections.Specialized.NameValueCollection query = Query(await Login(flows));
        FlowTakeResult taken = await flows.TakeAsync(query["state"]!, default);

        Assert.Equal(query["code_challenge"], OAuthClient.Challenge(taken.Flow!.Value.Verifier));
    }

    [Fact]
    public async Task GetLogin_RecordsTheFlowWithTheConfiguredLifetime()
    {
        var clock = new FixedClock(Now);
        var flows = new RecordingFlowStore();

        await Login(flows, clock);

        SignInOptions options = Options();
        Assert.Equal(Now + options.FlowLifetime, flows.ExpiresAt);
    }

    [Fact]
    public async Task GetLogin_SanitisesTheReturnPath()
    {
        var flows = new RecordingFlowStore();

        await Login(flows, returnTo: "https://evil.test/steal");

        Assert.Equal("/", flows.ReturnTo);
    }

    [Fact]
    public async Task GetLogin_KeepsARelativeReturnPath()
    {
        var flows = new RecordingFlowStore();

        await Login(flows, returnTo: "/dashboard?tab=2");

        Assert.Equal("/dashboard?tab=2", flows.ReturnTo);
    }

    [Fact]
    public async Task GetLogin_MarksTheResponseNoStore()
    {
        var flows = new InMemoryLoginFlowStore(new FixedClock(Now));
        DefaultHttpContext context = new DefaultHttpContext().Executable();

        await Run(context, flows, new FixedClock(Now), Options());

        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task GetLogin_UnconfiguredHost_DegradesToTheFailureCode()
    {
        var flows = new RecordingFlowStore();
        DefaultHttpContext context = new DefaultHttpContext().Executable();
        var blank = new SignInOptions
        {
            StoreEncryptionKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
        };
        blank.Validate();

        await Run(context, flows, new FixedClock(Now), blank);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal(
            "/?error=provider_unavailable",
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task GetLogin_UnconfiguredHost_RecordsNoFlow()
    {
        var flows = new RecordingFlowStore();
        var blank = new SignInOptions
        {
            StoreEncryptionKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
        };
        blank.Validate();

        await Run(new DefaultHttpContext().Executable(), flows, new FixedClock(Now), blank);

        Assert.Equal(0, flows.Starts);
    }

    private static async Task<string> Login(
        ILoginFlowStore flows,
        TimeProvider? clock = null,
        string? returnTo = null)
    {
        DefaultHttpContext context = new DefaultHttpContext().Executable();

        if (returnTo is not null)
        {
            context.Request.QueryString = new QueryString(
                "?returnTo=" + Uri.EscapeDataString(returnTo));
        }

        await Run(context, flows, clock ?? new FixedClock(Now), Options());

        return context.Response.Headers.Location.ToString();
    }

    private static async Task Run(
        DefaultHttpContext context,
        ILoginFlowStore flows,
        TimeProvider clock,
        SignInOptions options)
    {
        var client = new OAuthClient(
            new HttpClient(new RefusingHandler()), options, NullLogger<OAuthClient>.Instance);

        IResult result = await SignInEndpoints.GetLoginAsync(
            context, client, flows, options, clock, NullLoggerFactory.Instance);

        await result.ExecuteAsync(context);
    }

    private static System.Collections.Specialized.NameValueCollection Query(string location) =>
        HttpUtility.ParseQueryString(new Uri(location).Query);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingFlowStore : ILoginFlowStore
    {
        public int Starts { get; private set; }

        public string? ReturnTo { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        public Task StartAsync(
            string state, string verifier, string returnTo, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Starts++;
            ReturnTo = returnTo;
            ExpiresAt = expiresAt;
            return Task.CompletedTask;
        }

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct) =>
            Task.FromResult(FlowTakeResult.Unknown());

        public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Beginning a flow must not call the provider: the browser does that.");
    }
}
