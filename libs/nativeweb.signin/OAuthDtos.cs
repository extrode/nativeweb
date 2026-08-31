using System.Text.Json.Serialization;

namespace NativeWeb.SignIn;

// The provider's wire shapes, and internal on purpose: they are the provider's format, not
// this library's contract. A public DTO here would make every field a provider adds — or
// spells differently — a breaking change for consumers.
//
// Every property carries an explicit [JsonPropertyName] and no naming policy is configured.
// The RFC field names are snake_case; a provider that also serves camelCase endpoints of its
// own would otherwise need a policy, and a policy can silently rename a field on one half of
// an API when it changes.
//
// Most fields are nullable because a provider may omit them, and a non-nullable record
// property would hand a null to code the compiler told was safe.
//
// Nothing here models a field the framework does not use. Unmodelled members are ignored by
// System.Text.Json's default behaviour, which is the whole of R31: a provider is free to add
// fields, and adding one must never break a sign-in.

/// <summary>A successful token response. RFC 6749 §5.1 field names.</summary>
internal sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("scope")] string? Scope);

/// <summary>An error body from any endpoint. RFC 6749 §5.2 field names.</summary>
/// <remarks>
/// <c>ErrorDescription</c> is for the log and NEVER for a page: it is upstream prose this
/// library does not control, cannot localise and would be rendering into someone else's HTML.
/// <c>Error</c> is mapped to a refusal reason before anything public sees it. Neither ever
/// leaves the client.
/// </remarks>
internal sealed record OAuthErrorResponse(
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);

/// <summary>
/// The userinfo resource. <c>sub</c> is the only field a local account may be keyed on — an
/// address can move between people, a subject cannot, and keying on email would let a
/// re-registered address inherit an existing account.
/// </summary>
internal sealed record UserInfo(
    [property: JsonPropertyName("sub")] string? Sub,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("email_verified")] bool EmailVerified,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>
/// A token introspection response. RFC 7662 §2.2 field names.
/// </summary>
/// <remarks>
/// <c>active</c> is the only field required of a provider, and the only one a refusal is
/// decided on. <c>Exp</c> is seconds since the Unix epoch and is read for one reason: it is
/// the ceiling on how long this result may be cached, so a cache can never hold a token past
/// the life the provider reported for it.
/// </remarks>
internal sealed record IntrospectionResponse(
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("sub")] string? Sub,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("client_id")] string? ClientId,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("exp")] long? Exp);

// Source-generated: the reflection-based JsonSerializer overloads trip IL2026/IL3050 under
// this repo's AOT gate, and every HTTP call in this library passes a JsonTypeInfo<T>.
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(OAuthErrorResponse))]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(IntrospectionResponse))]
internal sealed partial class OAuthJsonContext : JsonSerializerContext
{
}
