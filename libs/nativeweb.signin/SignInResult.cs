namespace NativeWeb.SignIn;

/// <summary>
/// The outcome of a call to the identity provider: three cases that must stay three.
/// </summary>
/// <remarks>
/// <see cref="Match{TOut}"/> is the ONLY way to reach the value. There is no <c>IsOk</c>, no
/// <c>Value</c>, no conversion to <see cref="bool"/> — because the moment the API allows it, the
/// three cases collapse into a two-case check, and <see cref="SignInOutcome.Unavailable"/> (the
/// provider is down; degrade and retry) gets handled as <see cref="SignInOutcome.Refused"/> (the
/// answer was no; do not retry). A <c>switch</c> over <see cref="Outcome"/> that misses an arm is
/// CS8509, which this repo's <c>TreatWarningsAsErrors</c> turns into a build break.
/// <para>
/// No provider-supplied string appears on any public member. Neither <c>error_description</c> nor
/// <c>error</c> is ours to render: both are upstream text, and a public property named
/// <c>Message</c> reaches a page within a year. The raw code lives in an internal field the
/// library logs through its own logger.
/// </para>
/// </remarks>
/// <typeparam name="T">What a successful call returns.</typeparam>
public readonly struct SignInResult<T>
{
    private readonly T? _value;

    /// <summary>The provider's raw error code, for this library's log and nothing else.</summary>
    internal readonly string? ProviderError;

    private SignInResult(SignInOutcome outcome, RefusalReason reason, T? value, string? providerError)
    {
        Outcome = outcome;
        Reason = reason;
        _value = value;
        ProviderError = providerError;
    }

    /// <summary>Which of the three cases this is.</summary>
    public SignInOutcome Outcome { get; }

    /// <summary>
    /// Why the provider refused. Meaningful only when <see cref="Outcome"/> is
    /// <see cref="SignInOutcome.Refused"/>; <see cref="RefusalReason.None"/> otherwise.
    /// </summary>
    public RefusalReason Reason { get; }

    /// <summary>The call succeeded.</summary>
    internal static SignInResult<T> Ok(T value) =>
        new(SignInOutcome.Ok, RefusalReason.None, value, null);

    /// <summary>
    /// The provider answered, and the answer was no. <paramref name="providerError"/> is null when
    /// the provider sent a failure with no readable <c>error</c> field, which is a real thing a
    /// proxy does — a sentinel string in its place would eventually be read as the provider's own
    /// vocabulary.
    /// </summary>
    internal static SignInResult<T> Refused(RefusalReason reason, string? providerError) =>
        new(SignInOutcome.Refused, reason, default, providerError);

    /// <summary>The provider did not answer, or is not configured. Nothing was decided.</summary>
    internal static SignInResult<T> Unavailable(string? providerError) =>
        new(SignInOutcome.Unavailable, RefusalReason.None, default, providerError);

    /// <summary>
    /// Handles all three cases. Every delegate is mandatory, which is the point: there is no path
    /// that reaches <typeparamref name="T"/> without stating what happens when there is no
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TOut">What every branch produces.</typeparam>
    /// <param name="ok">Given the value, when the call succeeded.</param>
    /// <param name="refused">Given the library-owned reason, when the provider refused.</param>
    /// <param name="unavailable">When the provider could not be reached or is unconfigured.</param>
    /// <returns>Whatever the matching branch returned.</returns>
    /// <exception cref="ArgumentNullException">Any of the three delegates is null.</exception>
    public TOut Match<TOut>(Func<T, TOut> ok, Func<RefusalReason, TOut> refused, Func<TOut> unavailable)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(refused);
        ArgumentNullException.ThrowIfNull(unavailable);

        return Outcome switch
        {
            SignInOutcome.Ok => ok(_value!),
            SignInOutcome.Refused => refused(Reason),
            SignInOutcome.Unavailable => unavailable(),
            _ => throw new InvalidOperationException("Unrecognised outcome."),
        };
    }
}

/// <summary>The three cases a call to the provider can end in.</summary>
public enum SignInOutcome
{
    /// <summary>The provider answered and the answer carries a value.</summary>
    Ok,

    /// <summary>The provider answered and declined. Retrying changes nothing.</summary>
    Refused,

    /// <summary>No answer: unreachable, erroring, or unconfigured. Degrade, do not treat as a refusal.</summary>
    Unavailable,
}

/// <summary>
/// Why a call was refused, as a closed set this library owns.
/// </summary>
/// <remarks>
/// Mapped from the provider's error codes rather than passed through, so the provider cannot add a
/// case a consumer's <c>switch</c> has never seen; anything unrecognised collapses to
/// <see cref="Unknown"/>.
/// </remarks>
public enum RefusalReason
{
    /// <summary>
    /// Not a refusal. The value on every <see cref="SignInOutcome.Ok"/> and
    /// <see cref="SignInOutcome.Unavailable"/> result.
    /// </summary>
    /// <remarks>
    /// Explicitly zero so that <c>default(RefusalReason)</c> is "no reason" rather than whichever
    /// member happens to be declared first. A default that means <c>InvalidRequest</c> is a lie
    /// told by the type system.
    /// </remarks>
    None = 0,

    /// <summary>The request was malformed as far as the provider is concerned.</summary>
    InvalidRequest,

    /// <summary>The client credentials were rejected.</summary>
    InvalidClient,

    /// <summary>The code, verifier or grant was rejected — expired, replayed, or mismatched.</summary>
    InvalidGrant,

    /// <summary>The user declined, or the provider's policy said no.</summary>
    AccessDenied,

    /// <summary>A code this library does not map. Deliberately generic; the raw code is logged.</summary>
    Unknown,
}

/// <summary>Maps the provider's RFC 6749 error codes onto the closed set above.</summary>
internal static class RefusalReasons
{
    /// <summary>
    /// The mapped reason for <paramref name="providerError"/>. Anything absent, empty or
    /// unrecognised is <see cref="RefusalReason.Unknown"/> — a provider is free to invent codes,
    /// and none of them may reach a consumer's <c>switch</c> as a new case.
    /// </summary>
    /// <param name="providerError">The provider's <c>error</c> field, if it sent one.</param>
    internal static RefusalReason From(string? providerError) =>
        providerError switch
        {
            "invalid_request" => RefusalReason.InvalidRequest,
            "invalid_client" or "unauthorized_client" => RefusalReason.InvalidClient,
            "invalid_grant" => RefusalReason.InvalidGrant,
            "access_denied" => RefusalReason.AccessDenied,
            _ => RefusalReason.Unknown,
        };
}
