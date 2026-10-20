using System.Net;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class OAuthClientRefreshTests
{
    private const string RotatedJson =
        "{\"access_token\":\"at-2\",\"token_type\":\"Bearer\",\"expires_in\":1800,\"refresh_token\":\"rt-2\"}";

    [Fact]
    public async Task RefreshAsync_PostsTheRefreshGrant()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, RotatedJson));
        OAuthClient client = ClientFor(handler);

        await client.RefreshAsync("rt-1", default);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://provider.test/token", handler.RequestUri);
        Assert.Equal("refresh_token", handler.Form["grant_type"]);
        Assert.Equal("rt-1", handler.Form["refresh_token"]);
    }

    [Fact]
    public async Task RefreshAsync_Success_ReturnsTheRotatedPair()
    {
        OAuthClient client = ClientFor(new StubHandler(Json(HttpStatusCode.OK, RotatedJson)));

        SignInResult<ProviderTokens> result = await client.RefreshAsync("rt-1", default);

        ProviderTokens tokens = Value(result);
        Assert.Equal("at-2", tokens.AccessToken);
        Assert.Equal("rt-2", tokens.RefreshToken);
        Assert.Equal(1800, tokens.ExpiresIn);
    }

    [Fact]
    public async Task RefreshAsync_ProviderRefusal_IsRefusedRatherThanThrown()
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"reused\"}"));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderTokens> result = await client.RefreshAsync("rt-1", default);

        Assert.Equal(SignInOutcome.Refused, result.Outcome);
        Assert.Equal(RefusalReason.InvalidGrant, result.Reason);
    }

    [Fact]
    public async Task RefreshAsync_ProviderDown_IsUnavailable()
    {
        OAuthClient client = ClientFor(new StubHandler(new HttpRequestException("no route")));

        SignInResult<ProviderTokens> result = await client.RefreshAsync("rt-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task RefreshAsync_BasicAuthentication_SendsNoSecretInTheBody()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, RotatedJson));
        OAuthClient client = ClientFor(handler, ClientAuthenticationMode.ClientSecretBasic);

        await client.RefreshAsync("rt-1", default);

        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.DoesNotContain("client-secret", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntrospectAsync_PostsTheTokenAsAFormFieldWithClientCredentials()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, "{\"active\":true,\"sub\":\"subject-1\"}"));
        OAuthClient client = ClientFor(handler);

        await client.IntrospectAsync("at-1", default);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://provider.test/introspect", handler.RequestUri);
        Assert.Equal("at-1", handler.Form["token"]);
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.Equal("client-id:client-secret", Decode(handler.AuthorizationParameter!));
    }

    [Fact]
    public async Task IntrospectAsync_PostAuthentication_CarriesTheCredentialsInTheForm()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, "{\"active\":true}"));
        OAuthClient client = ClientFor(handler, ClientAuthenticationMode.ClientSecretPost);

        await client.IntrospectAsync("at-1", default);

        Assert.Null(handler.AuthorizationScheme);
        Assert.Equal("client-id", handler.Form["client_id"]);
        Assert.Equal("client-secret", handler.Form["client_secret"]);
    }

    [Fact]
    public async Task IntrospectAsync_ActiveToken_CarriesTheSubjectScopeAndExpiry()
    {
        var handler = new StubHandler(Json(
            HttpStatusCode.OK,
            "{\"active\":true,\"sub\":\"subject-1\",\"scope\":\"api read\",\"exp\":1893456000}"));
        OAuthClient client = ClientFor(handler);

        SignInResult<TokenIntrospection> result = await client.IntrospectAsync("at-1", default);

        TokenIntrospection introspection = Value(result);
        Assert.True(introspection.Active);
        Assert.Equal("subject-1", introspection.Subject);
        Assert.Equal("api read", introspection.Scope);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), introspection.ExpiresAt);
    }

    [Fact]
    public async Task IntrospectAsync_InactiveToken_IsAnAnswerRatherThanARefusal()
    {
        OAuthClient client = ClientFor(new StubHandler(Json(HttpStatusCode.OK, "{\"active\":false}")));

        SignInResult<TokenIntrospection> result = await client.IntrospectAsync("at-1", default);

        TokenIntrospection introspection = Value(result);
        Assert.False(introspection.Active);
        Assert.Null(introspection.ExpiresAt);
    }

    [Fact]
    public async Task IntrospectAsync_ClientCredentialsRejected_IsRefused()
    {
        var handler = new StubHandler(Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}"));
        OAuthClient client = ClientFor(handler);

        SignInResult<TokenIntrospection> result = await client.IntrospectAsync("at-1", default);

        Assert.Equal(SignInOutcome.Refused, result.Outcome);
        Assert.Equal(RefusalReason.InvalidClient, result.Reason);
    }

    [Fact]
    public async Task IntrospectAsync_WithNoIntrospectionEndpoint_IsUnavailableAndMakesNoRequest()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, "{\"active\":true}"));
        OAuthClient client = ClientFor(handler, introspectionEndpoint: "");

        SignInResult<TokenIntrospection> result = await client.IntrospectAsync("at-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task IntrospectAsync_ProviderDown_IsUnavailable()
    {
        OAuthClient client = ClientFor(new StubHandler(Json(HttpStatusCode.BadGateway, "{}")));

        SignInResult<TokenIntrospection> result = await client.IntrospectAsync("at-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
    }
}
