using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SignInCallbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Tokens =
        "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\",\"expires_in\":1800}";

    private const string User = "{\"sub\":\"subject-1\",\"email\":\"a@example.test\"}";

    [Fact]
    public async Task Callback_ValidState_SendsTheBrowserToTheRecordedPath()
    {
        Fixture fixture = await Started(returnTo: "/dashboard");

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/dashboard", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_ValidState_MintsASession()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal(1, fixture.Sessions.Count);
        Assert.Contains(
            fixture.Options.CookieName,
            Assert.Single(context.Response.Headers.SetCookie!)!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_ValidState_SendsTheRecordedVerifierToTheTokenEndpoint()
    {
        Fixture fixture = await Started();

        await fixture.Callback();

        Assert.Equal(fixture.Verifier, fixture.Provider.Exchanges[0]["code_verifier"]);
        Assert.Equal("code-1", fixture.Provider.Exchanges[0]["code"]);
    }

    [Fact]
    public async Task Callback_ValidState_AsksTheProviderWhoSignedIn()
    {
        Fixture fixture = await Started();

        await fixture.Callback();

        Assert.Equal(1, fixture.Provider.UserInfoCalls);
    }

    [Fact]
    public async Task Callback_ReplayedState_IsRefusedWithoutATokenRequest()
    {
        Fixture fixture = await Started();
        await fixture.Callback();
        int exchangesAfterTheFirst = fixture.Provider.Exchanges.Count;

        DefaultHttpContext replay = await fixture.Callback();

        Assert.Equal(exchangesAfterTheFirst, fixture.Provider.Exchanges.Count);
        Assert.Equal("/signin-failed?error=state_used", replay.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_StateThisHostNeverMinted_IsRefusedWithoutATokenRequest()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(state: "never-minted");

        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal("/signin-failed?error=state_used", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_ExpiredState_IsRefusedAsExpired()
    {
        Fixture fixture = await Started();
        fixture.Clock.Now = Now.AddMinutes(16);

        DefaultHttpContext context = await fixture.Callback();

        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal("/signin-failed?error=state_expired", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_WithNoCode_IsIncomplete()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(code: "");

        Assert.Equal(
            "/signin-failed?error=callback_incomplete",
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_WithNoState_IsIncomplete()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(state: "");

        Assert.Equal(
            "/signin-failed?error=callback_incomplete",
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_OverlongCode_IsRefusedWithoutTakingTheFlow()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(code: new string('a', 513));

        Assert.Equal(
            "/signin-failed?error=callback_incomplete",
            context.Response.Headers.Location.ToString());
        Assert.Equal(FlowTake.Taken, (await fixture.Flows.TakeAsync(fixture.State, default)).Outcome);
    }

    [Fact]
    public async Task Callback_ProviderRefusedAtTheProvider_ReportsSignInFailed()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(code: "", error: "access_denied");

        Assert.Equal("/signin-failed?error=signin_failed", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_ProviderRefusesTheExchange_ReportsSignInFailedAndMintsNothing()
    {
        Fixture fixture = await Started();
        fixture.Provider.TokenResponse =
            () => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal("/signin-failed?error=signin_failed", context.Response.Headers.Location.ToString());
        Assert.Equal(0, fixture.Sessions.Count);
    }

    [Fact]
    public async Task Callback_ProviderUnreachable_ReportsUnavailable()
    {
        Fixture fixture = await Started();
        fixture.Provider.TokenResponse = () => Json(HttpStatusCode.BadGateway, "{}");

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal(
            "/signin-failed?error=provider_unavailable",
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_UserInfoRefuses_MintsNothing()
    {
        Fixture fixture = await Started();
        fixture.Provider.UserInfoResponse =
            () => Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_token\"}");

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal("/signin-failed?error=signin_failed", context.Response.Headers.Location.ToString());
        Assert.Equal(0, fixture.Sessions.Count);
    }

    [Fact]
    public async Task Callback_OffSiteReturnPathInTheStore_LandsOnTheFallback()
    {
        Fixture fixture = await Started(flows: new PoisonedFlowStore("https://evil.test/steal"));

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal("/", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_StoreReportsTakenWithNoFlow_IsRefusedRatherThanThrowing()
    {
        Fixture fixture = await Started(flows: new EmptyTakenFlowStore());

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal("/signin-failed?error=state_unknown", context.Response.Headers.Location.ToString());
        Assert.Empty(fixture.Provider.Exchanges);
    }

    [Fact]
    public async Task Callback_MarksTheResponseNoStore()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Callback_FailureRedirect_CarriesNoProviderText()
    {
        Fixture fixture = await Started();
        fixture.Provider.TokenResponse = () => Json(
            HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"the code was already redeemed\"}");

        DefaultHttpContext context = await fixture.Callback();

        Assert.DoesNotContain(
            "redeemed",
            context.Response.Headers.Location.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Fixture> Started(
        string returnTo = "/", ILoginFlowStore? flows = null)
    {
        SignInOptions options = Options();
        var clock = new MovableClock(Now);
        ILoginFlowStore flowStore = flows ?? new InMemoryLoginFlowStore(clock);
        var provider = new StubProvider();
        var client = new OAuthClient(
            new HttpClient(provider), options, NullLogger<OAuthClient>.Instance);
        var sessions = new InMemorySessionStore(clock);

        var manager = new SessionManager(
            sessions, client, options, clock, NullLogger<SessionManager>.Instance);

        DefaultHttpContext begin = new DefaultHttpContext().Executable();
        begin.Request.QueryString = new QueryString("?returnTo=" + Uri.EscapeDataString(returnTo));

        IResult redirect = await SignInEndpoints.GetLoginAsync(
            begin, client, flowStore, options, clock, NullLoggerFactory.Instance);
        await redirect.ExecuteAsync(begin);

        string state = System.Web.HttpUtility
            .ParseQueryString(new Uri(begin.Response.Headers.Location.ToString()).Query)["state"]!;

        string verifier = flowStore is InMemoryLoginFlowStore ? await VerifierFor(flowStore, state) : "";

        return new Fixture(client, flowStore, manager, sessions, provider, options, clock, state, verifier);
    }

    /// <summary>
    /// Reads the verifier back out and puts it straight in again, so the assertion that the
    /// exchange sends the recorded verifier has something to compare against without the endpoint
    /// having to hand it over.
    /// </summary>
    private static async Task<string> VerifierFor(ILoginFlowStore flows, string state)
    {
        FlowTakeResult taken = await flows.TakeAsync(state, default);
        string verifier = taken.Flow!.Value.Verifier;
        await flows.StartAsync(state, verifier, taken.Flow.Value.ReturnTo, Now.AddMinutes(15), default);

        return verifier;
    }

    private sealed record Fixture(
        OAuthClient Client,
        ILoginFlowStore Flows,
        SessionManager Manager,
        InMemorySessionStore Sessions,
        StubProvider Provider,
        SignInOptions Options,
        MovableClock Clock,
        string State,
        string Verifier)
    {
        internal async Task<DefaultHttpContext> Callback(
            string code = "code-1", string? state = null, string? error = null)
        {
            DefaultHttpContext context = new DefaultHttpContext().Executable();
            var query = new List<string>();

            if (code.Length > 0)
            {
                query.Add("code=" + Uri.EscapeDataString(code));
            }

            string effectiveState = state ?? State;

            if (effectiveState.Length > 0)
            {
                query.Add("state=" + Uri.EscapeDataString(effectiveState));
            }

            if (error is not null)
            {
                query.Add("error=" + Uri.EscapeDataString(error));
            }

            context.Request.QueryString = new QueryString(
                query.Count == 0 ? "" : "?" + string.Join('&', query));

            IResult result = await SignInEndpoints.GetCallbackAsync(
                context, Client, Flows, Manager, Options, NullLoggerFactory.Instance);
            await result.ExecuteAsync(context);

            return context;
        }
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubProvider : HttpMessageHandler
    {
        public List<Dictionary<string, string>> Exchanges { get; } = [];

        public int UserInfoCalls { get; private set; }

        public Func<HttpResponseMessage> TokenResponse { get; set; } =
            () => Json(HttpStatusCode.OK, Tokens);

        public Func<HttpResponseMessage> UserInfoResponse { get; set; } =
            () => Json(HttpStatusCode.OK, User);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/userinfo", StringComparison.Ordinal))
            {
                UserInfoCalls++;
                return UserInfoResponse();
            }

            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            System.Collections.Specialized.NameValueCollection form =
                System.Web.HttpUtility.ParseQueryString(body);

            Exchanges.Add(
                form.AllKeys.ToDictionary(k => k!, k => form[k]!, StringComparer.Ordinal));

            return TokenResponse();
        }
    }

    private sealed class PoisonedFlowStore(string returnTo) : ILoginFlowStore
    {
        public Task StartAsync(
            string state, string verifier, string to, DateTimeOffset expiresAt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct) =>
            Task.FromResult(FlowTakeResult.Taken(new LoginFlow("verifier-1", returnTo)));

        public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyTakenFlowStore : ILoginFlowStore
    {
        public Task StartAsync(
            string state, string verifier, string to, DateTimeOffset expiresAt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct) =>
            Task.FromResult(FlowTakeResult.Taken(new LoginFlow(null!, null!)));

        public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
