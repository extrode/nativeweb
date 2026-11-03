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

        // Two Set-Cookie headers now: the flow correlator being deleted, and the session being
        // written. The session cookie is the one this is about.
        Assert.Equal(1, fixture.Sessions.Count);
        Assert.Contains(
            context.Response.Headers.SetCookie!,
            header => header!.StartsWith(fixture.Options.CookieName + "=", StringComparison.Ordinal)
                && header.Length > fixture.Options.CookieName.Length + 1);
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
        Assert.Equal("/signin-failed?error=state_unknown", replay.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_StateThisHostNeverMinted_IsRefusedWithoutATokenRequest()
    {
        Fixture fixture = await Started();

        // The browser's own cookie carries the fabricated state too, so what refuses here is the
        // FLOW STORE having no such row -- not the correlation check upstream of it.
        DefaultHttpContext context = await fixture.Callback(
            "code-1", "never-minted", error: null, flowCookie: "never-minted");

        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal("/signin-failed?error=state_unknown", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_ExpiredStateWithFlowCookie_IsRefusedAsExpiredAndClearsIt()
    {
        Fixture fixture = await Started();
        fixture.Clock.Now = Now + fixture.Options.FlowLifetime + TimeSpan.FromMinutes(1);

        DefaultHttpContext context = await fixture.Callback();

        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal("/signin-failed?error=state_expired", context.Response.Headers.Location.ToString());
        Assert.Contains(
            context.Response.Headers.SetCookie!,
            header => header!.StartsWith(
                SessionManager.FlowCookieName(fixture.Options) + "=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_ExpiredAndSweptStateWithFlowCookie_IsRefusedAsUnknown()
    {
        Fixture fixture = await Started();
        fixture.Clock.Now = Now + fixture.Options.FlowLifetime + TimeSpan.FromMinutes(1);
        await fixture.Flows.SweepAsync(default);

        DefaultHttpContext context = await fixture.Callback();

        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal("/signin-failed?error=state_unknown", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_ExpiredStateWithNoFlowCookie_IsRefusedAsExpired()
    {
        Fixture fixture = await Started();
        fixture.Clock.Now = Now + fixture.Options.FlowLifetime + TimeSpan.FromMinutes(1);

        DefaultHttpContext context = await fixture.Callback(
            "code-1", state: null, error: null, flowCookie: null);

        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal("/signin-failed?error=state_expired", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_LiveStateWithNoFlowCookie_ConsumesTheFlowAndRefuses()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(
            "code-1", state: null, error: null, flowCookie: null);

        Assert.Equal("/signin-failed?error=state_mismatch", context.Response.Headers.Location.ToString());
        Assert.NotEqual(FlowTake.Taken, (await fixture.Flows.TakeAsync(fixture.State, default)).Outcome);
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
    public async Task GetLogin_FlowCookieOutlivesTheFlowByOneLifetime()
    {
        SignInOptions options = Options();
        var clock = new MovableClock(Now);
        var client = new OAuthClient(
            new HttpClient(new StubProvider()), options, NullLogger<OAuthClient>.Instance);
        DefaultHttpContext begin = new DefaultHttpContext().Executable();

        IResult redirect = await SignInEndpoints.GetLoginAsync(
            begin, client, new InMemoryLoginFlowStore(clock), options, clock, NullLoggerFactory.Instance);
        await redirect.ExecuteAsync(begin);

        string header = begin.Response.Headers.SetCookie!
            .Select(h => h!)
            .Single(h => h.StartsWith(SessionManager.FlowCookieName(options) + "=", StringComparison.Ordinal));
        string expires = header.Split(';').Select(p => p.Trim())
            .Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..];

        Assert.Equal(
            Now + (2 * options.FlowLifetime),
            DateTimeOffset.Parse(expires, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("%20")]
    [InlineData("long")]
    public async Task GetLogin_WithAnUnusableFlowCookie_DoesNotTakeAndStillStarts(string cookie)
    {
        SignInOptions options = Options();
        var clock = new MovableClock(Now);
        var client = new OAuthClient(
            new HttpClient(new StubProvider()), options, NullLogger<OAuthClient>.Instance);
        var flows = new RecordingFlowStore(new InMemoryLoginFlowStore(clock));
        DefaultHttpContext begin = new DefaultHttpContext().Executable();
        string value = cookie == "long" ? new string('a', 513) : cookie;
        begin.Request.Headers.Cookie = $"{SessionManager.FlowCookieName(options)}={value}";

        IResult redirect = await SignInEndpoints.GetLoginAsync(
            begin, client, flows, options, clock, NullLoggerFactory.Instance);
        await redirect.ExecuteAsync(begin);

        Assert.Empty(flows.Taken);
        Assert.Equal(1, flows.Started);
        Assert.Equal(302, begin.Response.StatusCode);
    }

    [Fact]
    public async Task Callback_WhitespaceState_IsIncomplete()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback("1", " ", error: null, flowCookie: null);

        Assert.Equal(
            "/signin-failed?error=callback_incomplete",
            context.Response.Headers.Location.ToString());
        Assert.Empty(fixture.Provider.Exchanges);
    }

    [Fact]
    public async Task Callback_WhitespaceCode_IsIncomplete()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(" ");

        Assert.Equal(
            "/signin-failed?error=callback_incomplete",
            context.Response.Headers.Location.ToString());
        Assert.Equal(FlowTake.Taken, (await fixture.Flows.TakeAsync(fixture.State, default)).Outcome);
    }

    [Fact]
    public async Task Callback_StoreReportsAlreadyUsed_RedirectsWithStateUsed()
    {
        Fixture fixture = await Started(flows: new AlreadyUsedFlowStore());

        DefaultHttpContext context = await fixture.Callback();

        Assert.Equal("/signin-failed?error=state_used", context.Response.Headers.Location.ToString());
        Assert.Empty(fixture.Provider.Exchanges);
    }

    [Fact]
    public async Task Callback_WithNoFlowCookie_IsRefusedWithoutATokenRequest()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(
            "code-1", state: null, error: null, flowCookie: null);

        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal(0, fixture.Sessions.Count);
        Assert.Equal("/signin-failed?error=state_mismatch", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_WithAnotherBrowsersState_IsRefusedWithoutATokenRequest()
    {
        // Login CSRF, made concrete: the attacker started the flow, so the state and code in this
        // callback are theirs and both are valid. The victim's browser holds no matching cookie.
        Fixture attacker = await Started();
        Fixture victim = await Started();

        DefaultHttpContext context = await victim.Callback(
            "code-1", attacker.State, error: null, flowCookie: victim.State);

        Assert.Empty(victim.Provider.Exchanges);
        Assert.Equal(0, victim.Sessions.Count);
        Assert.Equal("/signin-failed?error=state_mismatch", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task GetLogin_RecordsTheStateInTheBrowserToo()
    {
        Fixture fixture = await Started();

        Assert.Equal(fixture.State, fixture.FlowCookieOnTheWayOut);
    }

    [Fact]
    public async Task Callback_StaleState_LeavesTheFlowCookieAndTheNewerFlow()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback(
            "code-1", "stale-state", error: null, flowCookie: fixture.State);

        Assert.Equal("/signin-failed?error=state_mismatch", context.Response.Headers.Location.ToString());
        Assert.DoesNotContain(
            context.Response.Headers.SetCookie,
            header => header!.StartsWith(
                SessionManager.FlowCookieName(fixture.Options) + "=", StringComparison.Ordinal));
        Assert.Equal(FlowTake.Taken, (await fixture.Flows.TakeAsync(fixture.State, default)).Outcome);
    }

    [Fact]
    public async Task GetLogin_WithAnUnfinishedFlow_ConsumesItAndTheNewFlowCompletes()
    {
        Fixture fixture = await Started();

        string newer = await fixture.Restart();

        DefaultHttpContext stale = await fixture.Callback(
            "code-1", fixture.State, error: null, flowCookie: fixture.State);
        Assert.Empty(fixture.Provider.Exchanges);
        Assert.Equal("/signin-failed?error=state_unknown", stale.Response.Headers.Location.ToString());

        DefaultHttpContext current = await fixture.Callback(
            "code-1", newer, error: null, flowCookie: newer);
        Assert.Equal("/", current.Response.Headers.Location.ToString());
        Assert.Equal(1, fixture.Sessions.Count);
    }

    [Fact]
    public async Task Callback_Taken_ClearsTheFlowCookie()
    {
        Fixture fixture = await Started();

        DefaultHttpContext context = await fixture.Callback();

        Assert.Contains(
            context.Response.Headers.SetCookie!,
            header => header!.StartsWith(
                SessionManager.FlowCookieName(fixture.Options) + "=;", StringComparison.Ordinal));
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

        string flowCookie = begin.Response.Headers.SetCookie!
            .Select(header => header!)
            .Where(header => header.StartsWith(
                SessionManager.FlowCookieName(options) + "=", StringComparison.Ordinal))
            .Select(header => header[(header.IndexOf('=', StringComparison.Ordinal) + 1)..].Split(';')[0])
            .SingleOrDefault() ?? "";

        return new Fixture(
            client, flowStore, manager, sessions, provider, options, clock, state, verifier, flowCookie);
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
        string Verifier,
        string FlowCookieOnTheWayOut)
    {
        /// <summary>Begins a second sign-in from the same browser and returns its state.</summary>
        internal async Task<string> Restart()
        {
            DefaultHttpContext begin = new DefaultHttpContext().Executable();
            begin.Request.Headers.Cookie = $"{SessionManager.FlowCookieName(Options)}={State}";

            IResult redirect = await SignInEndpoints.GetLoginAsync(
                begin, Client, Flows, Options, Clock, NullLoggerFactory.Instance);
            await redirect.ExecuteAsync(begin);

            return System.Web.HttpUtility
                .ParseQueryString(new Uri(begin.Response.Headers.Location.ToString()).Query)["state"]!;
        }

        internal Task<DefaultHttpContext> Callback(
            string code = "code-1", string? state = null, string? error = null) =>
            Callback(code, state, error, flowCookie: State);

        internal async Task<DefaultHttpContext> Callback(
            string code, string? state, string? error, string? flowCookie)
        {
            DefaultHttpContext context = new DefaultHttpContext().Executable();

            if (flowCookie is not null)
            {
                context.Request.Headers.Cookie =
                    $"{SessionManager.FlowCookieName(Options)}={flowCookie}";
            }

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

    private sealed class RecordingFlowStore(ILoginFlowStore inner) : ILoginFlowStore
    {
        internal List<string> Taken { get; } = [];

        internal int Started { get; private set; }

        public Task StartAsync(
            string state, string verifier, string to, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Started++;

            return inner.StartAsync(state, verifier, to, expiresAt, ct);
        }

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct)
        {
            Taken.Add(state);

            return inner.TakeAsync(state, ct);
        }

        public Task SweepAsync(CancellationToken ct) => inner.SweepAsync(ct);
    }

    private sealed class AlreadyUsedFlowStore : ILoginFlowStore
    {
        public Task StartAsync(
            string state, string verifier, string to, DateTimeOffset expiresAt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct) =>
            Task.FromResult(FlowTakeResult.AlreadyUsed());

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
