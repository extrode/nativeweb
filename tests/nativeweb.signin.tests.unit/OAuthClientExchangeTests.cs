using System.Net;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class OAuthClientExchangeTests
{
    private const string TokenJson =
        "{\"access_token\":\"at-1\",\"token_type\":\"Bearer\",\"expires_in\":1800,\"refresh_token\":\"rt-1\"}";

    [Fact]
    public async Task ExchangeCodeAsync_Success_ReturnsTheTokenPair()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, TokenJson));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        ProviderTokens tokens = Value(result);
        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Equal("rt-1", tokens.RefreshToken);
        Assert.Equal(1800, tokens.ExpiresIn);
    }

    [Fact]
    public async Task ExchangeCodeAsync_PostsTheAuthorizationCodeGrant()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, TokenJson));
        OAuthClient client = ClientFor(handler);

        await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://provider.test/token", handler.RequestUri);
        Assert.Equal("authorization_code", handler.Form["grant_type"]);
        Assert.Equal("code-1", handler.Form["code"]);
        Assert.Equal("verifier-1", handler.Form["code_verifier"]);
        Assert.Equal("https://app.test/signin/callback", handler.Form["redirect_uri"]);
    }

    [Fact]
    public async Task ExchangeCodeAsync_BasicAuthentication_PutsCredentialsInTheHeaderAndNowhereElse()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, TokenJson));
        OAuthClient client = ClientFor(handler, ClientAuthenticationMode.ClientSecretBasic);

        await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.Equal("client-id:client-secret", Decode(handler.AuthorizationParameter!));
        Assert.False(handler.Form.ContainsKey("client_id"));
        Assert.False(handler.Form.ContainsKey("client_secret"));
        Assert.DoesNotContain("client-secret", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeCodeAsync_BasicAuthentication_FormEncodesBothHalvesBeforeBase64()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, TokenJson));
        SignInOptions options = Options(ClientAuthenticationMode.ClientSecretBasic);
        options.ClientId = "client id/1";
        options.ClientSecret = "s3cr3t+with/base64=padding";
        var client = new OAuthClient(
            new HttpClient(handler),
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OAuthClient>.Instance);

        await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(
            "client%20id%2F1:s3cr3t%2Bwith%2Fbase64%3Dpadding",
            Decode(handler.AuthorizationParameter!));
    }

    [Fact]
    public async Task ExchangeCodeAsync_PostAuthentication_PutsCredentialsInTheFormAndNowhereElse()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, TokenJson));
        OAuthClient client = ClientFor(handler, ClientAuthenticationMode.ClientSecretPost);

        await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Null(handler.AuthorizationScheme);
        Assert.Equal("client-id", handler.Form["client_id"]);
        Assert.Equal("client-secret", handler.Form["client_secret"]);
    }

    [Fact]
    public async Task ExchangeCodeAsync_PublicClient_SendsNoSecretAnywhere()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, TokenJson));
        OAuthClient client = ClientFor(handler, ClientAuthenticationMode.None);

        await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Null(handler.AuthorizationScheme);
        Assert.Equal("client-id", handler.Form["client_id"]);
        Assert.False(handler.Form.ContainsKey("client_secret"));
        Assert.DoesNotContain("client-secret", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid_grant", RefusalReason.InvalidGrant)]
    [InlineData("invalid_client", RefusalReason.InvalidClient)]
    [InlineData("access_denied", RefusalReason.AccessDenied)]
    [InlineData("some_new_code", RefusalReason.Unknown)]
    public async Task ExchangeCodeAsync_ProviderRefusal_IsRefusedWithTheMappedReason(
        string error, RefusalReason expected)
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.BadRequest, $"{{\"error\":\"{error}\",\"error_description\":\"upstream prose\"}}"));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Refused, result.Outcome);
        Assert.Equal(expected, result.Reason);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ServerError_IsUnavailableRatherThanRefused()
    {
        var handler = new StubHandler(Json(HttpStatusCode.InternalServerError, "{\"error\":\"server_error\"}"));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
        Assert.Equal(RefusalReason.None, result.Reason);
    }

    [Fact]
    public async Task ExchangeCodeAsync_NonJsonErrorBody_IsStillRefused()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html>gateway said no</html>"),
        };
        OAuthClient client = ClientFor(new StubHandler(response));

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Refused, result.Outcome);
        Assert.Equal(RefusalReason.Unknown, result.Reason);
    }

    [Fact]
    public async Task ExchangeCodeAsync_TransportFailure_IsUnavailable()
    {
        var handler = new StubHandler(new HttpRequestException("connection refused"));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task ExchangeCodeAsync_Timeout_IsUnavailable()
    {
        var handler = new StubHandler(new TaskCanceledException("timed out"));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task ExchangeCodeAsync_UnreadableSuccessBody_IsUnavailable()
    {
        OAuthClient client = ClientFor(new StubHandler(Json(HttpStatusCode.OK, "{ not json")));

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task ExchangeCodeAsync_SuccessWithoutAnAccessToken_IsUnavailable()
    {
        OAuthClient client = ClientFor(new StubHandler(Json(HttpStatusCode.OK, "{\"expires_in\":60}")));

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task ExchangeCodeAsync_Unconfigured_IsUnavailableAndMakesNoRequest()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, TokenJson));
        var client = new OAuthClient(
            new HttpClient(handler),
            new SignInOptions { AllowEphemeralStoreEncryptionKey = true },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OAuthClient>.Instance);

        SignInResult<ProviderTokens> result = await client.ExchangeCodeAsync("code-1", "verifier-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, handler.Calls);
        Assert.False(client.IsConfigured);
    }

    [Fact]
    public async Task GetUserInfoAsync_Success_ReturnsTheSubject()
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.OK, "{\"sub\":\"subject-1\",\"email\":\"a@example.test\",\"name\":\"A\"}"));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderUser> result = await client.GetUserInfoAsync("at-1", default);

        ProviderUser user = Value(result);
        Assert.Equal("subject-1", user.Subject);
        Assert.Equal("a@example.test", user.Email);
        Assert.Equal("A", user.Name);
        Assert.Equal("https://provider.test/userinfo", handler.RequestUri);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("at-1", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task GetUserInfoAsync_NoSubject_IsUnavailable()
    {
        OAuthClient client = ClientFor(new StubHandler(Json(HttpStatusCode.OK, "{\"email\":\"a@example.test\"}")));

        SignInResult<ProviderUser> result = await client.GetUserInfoAsync("at-1", default);

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task GetUserInfoAsync_Refused_CarriesNoProviderProse()
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_token\",\"error_description\":\"revoked\"}"));
        OAuthClient client = ClientFor(handler);

        SignInResult<ProviderUser> result = await client.GetUserInfoAsync("at-1", default);

        Assert.Equal(SignInOutcome.Refused, result.Outcome);
        Assert.DoesNotContain("revoked", $"{result.Outcome}{result.Reason}", StringComparison.Ordinal);
    }

    [Fact]
    public void Challenge_MatchesTheRfc7636Vector()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            OAuthClient.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void NewVerifierAndNewState_AreUnguessableAndUrlSafe()
    {
        string[] verifiers = [.. Enumerable.Range(0, 32).Select(_ => OAuthClient.NewVerifier())];
        string[] states = [.. Enumerable.Range(0, 32).Select(_ => OAuthClient.NewState())];

        Assert.Equal(32, verifiers.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(32, states.Distinct(StringComparer.Ordinal).Count());

        foreach (string value in verifiers.Concat(states))
        {
            Assert.Equal(43, value.Length);
            Assert.All(value, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_', $"'{c}'"));
        }
    }

    [Fact]
    public void ProviderTokensToString_RedactsBothTokens()
    {
        string text = new ProviderTokens("at-secret", "rt-secret", 60).ToString();

        Assert.DoesNotContain("at-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rt-secret", text, StringComparison.Ordinal);
        Assert.Contains("60", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderUserToString_RedactsThePersonalData()
    {
        string text = new ProviderUser("subject-1", "a@example.test", "A Person").ToString();

        Assert.DoesNotContain("a@example.test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("A Person", text, StringComparison.Ordinal);
        Assert.Contains("subject-1", text, StringComparison.Ordinal);
    }
}
