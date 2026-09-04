using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.DataProtection;

namespace NativeWeb.Forms;

/// <summary>
/// Why a set of carried answers could not be read back.
/// </summary>
public enum CarriedAnswersFailure
{
    /// <summary>It was read back.</summary>
    None,

    /// <summary>The field was absent or blank.</summary>
    Absent,

    /// <summary>The value did not authenticate: it was altered, or made elsewhere.</summary>
    Altered,

    /// <summary>The value authenticated and its lifetime has passed.</summary>
    Expired,
}

/// <summary>
/// Carries a step of a multi-step form from one request to the next, in one opaque
/// field the client cannot read or alter.
/// </summary>
/// <remarks>
/// <para>
/// <b>No server state, deliberately.</b> The alternative is a keyed store of
/// part-finished forms, which needs a backing implementation, an expiry sweep and a
/// story for more than one instance. What a two-step form needs is the first step's
/// answers back, and they will fit in a hidden field. Spec 027 R6.
/// </para>
/// <para>
/// <b>Why not <c>ToTimeLimitedDataProtector</c>.</b> That is the framework's own
/// expiring protector and it would do the job, but it raises
/// <see cref="CryptographicException"/> for an expired value and for a tampered one
/// alike, so a caller cannot tell them apart. R7 wants a different message for each:
/// "start again, this took too long" is an instruction, "this was altered" is a
/// refusal. The expiry is therefore carried inside the protected payload and checked
/// here, and the protector's only job is authentication.
/// </para>
/// <para>
/// <b>The payload is length-prefixed, not JSON.</b> Nothing here may reach for a
/// reflection-based serializer: this assembly is published NativeAOT with
/// <c>TreatWarningsAsErrors</c>, and the trim analyzer is right to complain. Length
/// prefixes also mean no value needs escaping, so an answer containing the delimiter
/// round-trips unchanged -- which a delimiter-joined format would get wrong exactly
/// once, in production, on the answer that happened to contain one.
/// </para>
/// </remarks>
public sealed class CarriedAnswers
{
    // Any string would do; this one is namespaced so a value made for one purpose in
    // a host cannot be replayed into another.
    private const string Purpose = "NativeWeb.Forms.CarriedAnswers.v1";

    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Creates the helper over a host's configured data protection.
    /// </summary>
    /// <param name="provider">The host's data protection provider.</param>
    /// <param name="clock">The clock, for tests. Defaults to the system clock.</param>
    public CarriedAnswers(IDataProtectionProvider provider, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _protector = provider.CreateProtector(Purpose);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Protects a set of answers for one hidden field.
    /// </summary>
    /// <param name="answers">The answers to carry. May be empty.</param>
    /// <param name="lifetime">How long the value may be presented back.</param>
    /// <returns>The value to put in a hidden field.</returns>
    public string Protect(IReadOnlyDictionary<string, string> answers, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var payload = new StringBuilder();
        Append(payload, _clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture));

        foreach (KeyValuePair<string, string> answer in answers)
        {
            Append(payload, answer.Key);
            Append(payload, answer.Value);
        }

        return _protector.Protect(payload.ToString());
    }

    /// <summary>
    /// Reads a set of answers back, saying why when it cannot.
    /// </summary>
    /// <param name="carried">The value the form posted back.</param>
    /// <param name="answers">The answers, or empty when this returns false.</param>
    /// <param name="failure">Why it could not be read, or <see cref="CarriedAnswersFailure.None"/>.</param>
    /// <returns>Whether the answers were read back.</returns>
    public bool TryUnprotect(
        string? carried,
        out IReadOnlyDictionary<string, string> answers,
        out CarriedAnswersFailure failure)
    {
        answers = EmptyAnswers;

        if (string.IsNullOrWhiteSpace(carried))
        {
            failure = CarriedAnswersFailure.Absent;
            return false;
        }

        string payload;

        try
        {
            payload = _protector.Unprotect(carried);
        }
        catch (CryptographicException)
        {
            // Every rejection the protector makes -- a changed byte, a value from
            // another purpose, a value from a host whose keys this one does not have.
            // None of them is distinguishable from the others and none needs to be.
            failure = CarriedAnswersFailure.Altered;
            return false;
        }

        int at = 0;

        if (!TryRead(payload, ref at, out string? expiry)
            || !long.TryParse(expiry, NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiresAt))
        {
            // Authenticated but unreadable, which this code cannot produce. It means
            // a payload written by another version of this type, so it is refused the
            // way an altered one is rather than being half-read.
            failure = CarriedAnswersFailure.Altered;
            return false;
        }

        if (_clock.GetUtcNow().ToUnixTimeSeconds() > expiresAt)
        {
            failure = CarriedAnswersFailure.Expired;
            return false;
        }

        var read = new Dictionary<string, string>(StringComparer.Ordinal);

        while (at < payload.Length)
        {
            if (!TryRead(payload, ref at, out string? key) || !TryRead(payload, ref at, out string? value))
            {
                failure = CarriedAnswersFailure.Altered;
                return false;
            }

            read[key] = value;
        }

        answers = read;
        failure = CarriedAnswersFailure.None;
        return true;
    }

    private static readonly Dictionary<string, string> EmptyAnswers = new(StringComparer.Ordinal);

    private static void Append(StringBuilder payload, string value) =>
        payload.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);

    private static bool TryRead(string payload, ref int at, out string value)
    {
        value = string.Empty;

        int colon = payload.IndexOf(':', at);

        if (colon < 0
            || !int.TryParse(
                payload.AsSpan(at, colon - at), NumberStyles.Integer, CultureInfo.InvariantCulture, out int length)
            || length < 0
            || colon + 1 + length > payload.Length)
        {
            return false;
        }

        value = payload.Substring(colon + 1, length);
        at = colon + 1 + length;
        return true;
    }
}
