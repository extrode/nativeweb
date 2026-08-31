using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class BearerIntrospectionFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Active =
        "{\"active\":true,\"sub\":\"subject-1\",\"scope\":\"api\",\"client_id\":\"client-id\"}";

    private const string Inactive = "{\"active\":false}";

    [Fact]
    public async Task Invoke_ActiveToken_ReachesTheEndpoint()
    {
        Fixture fixture = Filter();

        Outcome outcome = await fixture.Call("token-1");

        Assert.True(outcome.ReachedTheEndpoint);
        Assert.Equal(200, outcome.Context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_ActiveToken_CarriesTheSubject()
    {
        Fixture fixture = Filter();

        Outcome outcome = await fixture.Call("token-1");

        Assert.Equal(
            "subject-1",
            outcome.Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task Invoke_InactiveToken_NeverReachesTheEndpoint()
    {
        Fixture fixture = Filter(() => Json(HttpStatusCode.OK, Inactive));

        Outcome outcome = await fixture.Call("token-1");

        Assert.False(outcome.ReachedTheEndpoint);
        Assert.Equal(401, outcome.Context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_TwoRequestsInsideTheTtl_CallTheProviderOnce()
    {
        Fixture fixture = Filter();

        await fixture.Call("token-1");
        fixture.Clock.Now = Now.AddSeconds(29);
        await fixture.Call("token-1");

        Assert.Equal(1, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_TwoRequestsEitherSideOfTheTtl_CallTheProviderTwice()
    {
        Fixture fixture = Filter();

        await fixture.Call("token-1");
        fixture.Clock.Now = Now.AddSeconds(31);
        await fixture.Call("token-1");

        Assert.Equal(2, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_TwoDifferentTokens_EachAskTheProvider()
    {
        Fixture fixture = Filter();

        await fixture.Call("token-1");
        await fixture.Call("token-2");

        Assert.Equal(2, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_TokenExpiringInsideTheTtl_IsNotServedFromCachePastItsExp()
    {
        Fixture fixture = Filter(
            () => Json(
                HttpStatusCode.OK,
                "{\"active\":true,\"sub\":\"subject-1\",\"exp\":"
                + Now.AddSeconds(5).ToUnixTimeSeconds()
                + "}"));

        await fixture.Call("token-1");
        fixture.Clock.Now = Now.AddSeconds(6);
        await fixture.Call("token-1");

        Assert.Equal(2, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_TokenExpiringInsideTheTtl_IsStillServedBeforeIt()
    {
        Fixture fixture = Filter(
            () => Json(
                HttpStatusCode.OK,
                "{\"active\":true,\"sub\":\"subject-1\",\"exp\":"
                + Now.AddSeconds(5).ToUnixTimeSeconds()
                + "}"));

        await fixture.Call("token-1");
        fixture.Clock.Now = Now.AddSeconds(4);
        await fixture.Call("token-1");

        Assert.Equal(1, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_TokenExpiringPastTheTtl_StillHonoursTheTtl()
    {
        Fixture fixture = Filter(
            () => Json(
                HttpStatusCode.OK,
                "{\"active\":true,\"sub\":\"subject-1\",\"exp\":"
                + Now.AddHours(1).ToUnixTimeSeconds()
                + "}"));

        await fixture.Call("token-1");
        fixture.Clock.Now = Now.AddSeconds(31);
        await fixture.Call("token-1");

        Assert.Equal(2, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_WithACacheLifetimeOfZero_AsksEveryTime()
    {
        Fixture fixture = Filter(cacheLifetime: TimeSpan.Zero);

        await fixture.Call("token-1");
        await fixture.Call("token-1");
        await fixture.Call("token-1");

        Assert.Equal(3, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_InactiveToken_IsCachedToo()
    {
        Fixture fixture = Filter(() => Json(HttpStatusCode.OK, Inactive));

        await fixture.Call("token-1");
        await fixture.Call("token-1");

        Assert.Equal(1, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_UnknownRevokedAndExpiredTokens_AreIndistinguishable()
    {
        Response unknown = await RefusalFor(() => Json(HttpStatusCode.OK, Inactive));
        Response revoked = await RefusalFor(
            () => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_token\"}"));
        Response expired = await RefusalFor(
            () => Json(
                HttpStatusCode.OK,
                "{\"active\":false,\"exp\":" + Now.AddHours(-1).ToUnixTimeSeconds() + "}"));

        Assert.Equal(unknown, revoked);
        Assert.Equal(unknown, expired);
    }

    [Fact]
    public async Task Invoke_AnAbsentHeader_IsTheSameRefusalAsABadToken()
    {
        Response refusedToken = await RefusalFor(() => Json(HttpStatusCode.OK, Inactive));

        Fixture fixture = Filter();
        Outcome outcome = await fixture.Call(token: null);

        Assert.Equal(refusedToken, Response.Of(outcome.Context));
    }

    [Fact]
    public async Task Invoke_AnotherScheme_IsRefusedWithoutAskingTheProvider()
    {
        Fixture fixture = Filter();

        Outcome outcome = await fixture.Call(header: "Basic dXNlcjpwYXNz");

        Assert.Equal(401, outcome.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Invoke_ALowercaseScheme_IsAccepted()
    {
        Fixture fixture = Filter();

        Outcome outcome = await fixture.Call(header: "bearer token-1");

        Assert.True(outcome.ReachedTheEndpoint);
    }

    [Fact]
    public async Task Invoke_ProviderUnreachable_IsUnavailableRatherThanUnauthorized()
    {
        Fixture fixture = Filter(() => Json(HttpStatusCode.BadGateway, "{}"));

        Outcome outcome = await fixture.Call("token-1");

        Assert.Equal(503, outcome.Context.Response.StatusCode);
        Assert.False(outcome.ReachedTheEndpoint);
    }

    [Fact]
    public async Task Invoke_ProviderUnreachable_CachesNothing()
    {
        Fixture fixture = Filter(() => Json(HttpStatusCode.BadGateway, "{}"));

        await fixture.Call("token-1");
        await fixture.Call("token-1");

        Assert.Equal(2, fixture.Provider.Calls);
        Assert.Equal(0, fixture.Cache.Count);
    }

    [Fact]
    public async Task Invoke_TheCacheIsSharedBetweenFilters()
    {
        Fixture first = Filter();
        Fixture second = Filter(cache: first.Cache, provider: first.Provider, clock: first.Clock);

        await first.Call("token-1");
        await second.Call("token-1");

        Assert.Equal(1, first.Provider.Calls);
    }

    private static async Task<Response> RefusalFor(Func<HttpResponseMessage> response)
    {
        Fixture fixture = Filter(response);
        Outcome outcome = await fixture.Call("token-1");

        return Response.Of(outcome.Context);
    }

    private static Fixture Filter(
        Func<HttpResponseMessage>? response = null,
        TimeSpan? cacheLifetime = null,
        IntrospectionCache? cache = null,
        CountingProvider? provider = null,
        MovableClock? clock = null)
    {
        SignInOptions options = Options();
        options.IntrospectionCacheLifetime = cacheLifetime ?? TimeSpan.FromSeconds(30);

        CountingProvider handler = provider
            ?? new CountingProvider(response ?? (() => Json(HttpStatusCode.OK, Active)));
        MovableClock time = clock ?? new MovableClock(Now);
        IntrospectionCache shared = cache ?? new IntrospectionCache();

        var filter = new BearerIntrospectionFilter(
            new OAuthClient(new HttpClient(handler), options, NullLogger<OAuthClient>.Instance),
            shared,
            options,
            time,
            NullLogger<BearerIntrospectionFilter>.Instance);

        return new Fixture(filter, handler, shared, time, options);
    }

    private sealed record Fixture(
        BearerIntrospectionFilter Under,
        CountingProvider Provider,
        IntrospectionCache Cache,
        MovableClock Clock,
        SignInOptions Options)
    {
        internal async Task<Outcome> Call(string? token = null, string? header = null)
        {
            DefaultHttpContext context = new DefaultHttpContext().Executable();

            if (header is not null)
            {
                context.Request.Headers.Authorization = header;
            }
            else if (token is not null)
            {
                context.Request.Headers.Authorization = "Bearer " + token;
            }

            bool reached = false;

            object? result = await Under.InvokeAsync(
                EndpointFilterInvocationContext.Create(context),
                _ =>
                {
                    reached = true;
                    return ValueTask.FromResult<object?>(Results.Ok());
                });

            if (result is IResult toExecute)
            {
                await toExecute.ExecuteAsync(context);
            }

            return new Outcome(context, reached);
        }
    }

    private sealed record Outcome(DefaultHttpContext Context, bool ReachedTheEndpoint);

    private sealed record Response(int Status, string Headers, long BodyLength)
    {
        internal static Response Of(DefaultHttpContext context) =>
            new(
                context.Response.StatusCode,
                string.Join(
                    '|',
                    context.Response.Headers
                        .Select(h => h.Key + "=" + h.Value.ToString())
                        .OrderBy(h => h, StringComparer.Ordinal)),
                context.Response.Body.Length);
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingProvider(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response());
        }
    }
}
