using System.Text.Json;

using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class OAuthDtosTests
{
    private const string Unmodelled =
        "\"realm\": \"acme\", \"realm_id\": \"01J\", \"operator\": {\"id\": 4}, "
        + "\"enrollment_credential_id\": \"cred-9\"";

    [Fact]
    public void TokenResponse_WithUnmodelledFields_Deserializes()
    {
        string json = $$"""
            {
              "access_token": "at-1",
              "token_type": "Bearer",
              "expires_in": 1800,
              "refresh_token": "rt-1",
              "scope": "openid profile",
              {{Unmodelled}}
            }
            """;

        TokenResponse? token = JsonSerializer.Deserialize(json, OAuthJsonContext.Default.TokenResponse);

        Assert.NotNull(token);
        Assert.Equal("at-1", token!.AccessToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(1800, token.ExpiresIn);
        Assert.Equal("rt-1", token.RefreshToken);
        Assert.Equal("openid profile", token.Scope);
    }

    [Fact]
    public void UserInfo_WithUnmodelledFields_Deserializes()
    {
        string json = $$"""
            {
              "sub": "subject-1",
              "email": "person@example.test",
              "email_verified": true,
              "name": "A Person",
              {{Unmodelled}}
            }
            """;

        UserInfo? info = JsonSerializer.Deserialize(json, OAuthJsonContext.Default.UserInfo);

        Assert.NotNull(info);
        Assert.Equal("subject-1", info!.Sub);
        Assert.Equal("person@example.test", info.Email);
        Assert.True(info.EmailVerified);
        Assert.Equal("A Person", info.Name);
    }

    [Fact]
    public void IntrospectionResponse_WithUnmodelledFields_Deserializes()
    {
        string json = $$"""
            {
              "active": true,
              "sub": "subject-1",
              "scope": "api",
              "client_id": "client-1",
              "token_type": "Bearer",
              "exp": 1893456000,
              {{Unmodelled}}
            }
            """;

        IntrospectionResponse? introspection =
            JsonSerializer.Deserialize(json, OAuthJsonContext.Default.IntrospectionResponse);

        Assert.NotNull(introspection);
        Assert.True(introspection!.Active);
        Assert.Equal("subject-1", introspection.Sub);
        Assert.Equal("api", introspection.Scope);
        Assert.Equal("client-1", introspection.ClientId);
        Assert.Equal(1893456000L, introspection.Exp);
    }

    [Fact]
    public void OAuthErrorResponse_WithUnmodelledFields_Deserializes()
    {
        string json = $$"""
            { "error": "invalid_grant", "error_description": "expired", {{Unmodelled}} }
            """;

        OAuthErrorResponse? error =
            JsonSerializer.Deserialize(json, OAuthJsonContext.Default.OAuthErrorResponse);

        Assert.NotNull(error);
        Assert.Equal("invalid_grant", error!.Error);
        Assert.Equal("expired", error.ErrorDescription);
    }

    [Fact]
    public void IntrospectionResponse_InactiveWithNothingElse_Deserializes()
    {
        IntrospectionResponse? introspection =
            JsonSerializer.Deserialize("{\"active\": false}", OAuthJsonContext.Default.IntrospectionResponse);

        Assert.NotNull(introspection);
        Assert.False(introspection!.Active);
        Assert.Null(introspection.Sub);
        Assert.Null(introspection.Exp);
    }

    [Fact]
    public void TokenResponse_OmittedOptionalFields_AreNull()
    {
        TokenResponse? token = JsonSerializer.Deserialize(
            "{\"access_token\": \"at-1\", \"expires_in\": 60}", OAuthJsonContext.Default.TokenResponse);

        Assert.NotNull(token);
        Assert.Null(token!.RefreshToken);
        Assert.Null(token.TokenType);
        Assert.Null(token.Scope);
    }

    [Fact]
    public void Members_BindByTheirRfcNamesOnly()
    {
        TokenResponse? token = JsonSerializer.Deserialize(
            "{\"accessToken\": \"at-1\", \"expiresIn\": 60}", OAuthJsonContext.Default.TokenResponse);

        Assert.NotNull(token);
        Assert.Null(token!.AccessToken);
        Assert.Equal(0, token.ExpiresIn);
    }
}
