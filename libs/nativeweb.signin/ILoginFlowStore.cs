namespace NativeWeb.SignIn;

/// <summary>
/// The at-most-once storage seam for an in-flight login. The library defines no
/// schema and issues no query; the consumer owns the table behind this interface.
/// </summary>
/// <remarks>
/// The single-use property lives entirely in <see cref="TakeAsync"/>, and it is a
/// contract, not advice:
/// <list type="bullet">
/// <item>
/// Returning <see cref="FlowTake.Taken"/> asserts that an ATOMIC compare-and-swap
/// consumed the row — one statement that both marks it used and reports whether this
/// caller was the one that marked it. A read-then-mark implementation compiles, passes
/// every single-threaded test, and completes a replayed callback TWICE under concurrency,
/// because both readers see an unused row.
/// </item>
/// <item>
/// Expiry is enforced INSIDE <see cref="TakeAsync"/>, not left to <see cref="SweepAsync"/>.
/// Sweep is garbage collection. An implementation that checks expiry only when sweeping
/// keeps unswept rows live forever and never returns <see cref="FlowTake.Expired"/>.
/// </item>
/// </list>
/// </remarks>
public interface ILoginFlowStore
{
    /// <summary>
    /// Records a newly started flow against its <c>state</c> value.
    /// </summary>
    /// <param name="state">The opaque state parameter sent to the provider.</param>
    /// <param name="verifier">The PKCE code verifier. Secret; never logged.</param>
    /// <param name="returnTo">Where to send the browser once sign-in completes.</param>
    /// <param name="expiresAt">
    /// The instant after which this flow must no longer be takeable. Computed by the library
    /// from <c>SignInOptions.FlowLifetime</c> and the application's <see cref="TimeProvider"/>,
    /// and passed in rather than left to the store so that one configured lifetime governs
    /// every implementation — and so a store never has to reach into the library's options to
    /// find out what it is.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task StartAsync(
        string state,
        string verifier,
        string returnTo,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    /// <summary>
    /// Consumes the flow for <paramref name="state"/>, atomically. A second call for the
    /// same state must return <see cref="FlowTake.AlreadyUsed"/>, including when the two
    /// calls race.
    /// </summary>
    /// <param name="state">The state parameter returned by the provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The outcome, carrying the flow only on <see cref="FlowTake.Taken"/>. An expired
    /// row yields <see cref="FlowTake.Expired"/> whether or not it has been swept.
    /// </returns>
    /// <remarks>
    /// Once a state has been taken, used or expired, no later call for that state may ever
    /// return <see cref="FlowTake.Taken"/> again. Which refusal it returns is deliberately
    /// NOT pinned: a swept row reads <see cref="FlowTake.Unknown"/> and an unswept one reads
    /// <see cref="FlowTake.AlreadyUsed"/> or <see cref="FlowTake.Expired"/>, and forcing
    /// those to agree would make sweeping a security-relevant operation, which is exactly
    /// what <see cref="SweepAsync"/> must never become. All three are refusals.
    /// </remarks>
    Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct);

    /// <summary>
    /// Deletes flows past their lifetime. Garbage collection only — no security property
    /// depends on this having run, and none may be moved here.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task SweepAsync(CancellationToken ct);
}

/// <summary>What a <see cref="ILoginFlowStore.TakeAsync"/> found.</summary>
public enum FlowTake
{
    /// <summary>This caller consumed the flow. Exactly one caller ever sees this per state.</summary>
    Taken,

    /// <summary>No such state: never started, already swept, or fabricated.</summary>
    Unknown,

    /// <summary>The state existed and was already consumed — a replayed callback link.</summary>
    AlreadyUsed,

    /// <summary>The state existed and was still unused, but its lifetime had passed.</summary>
    Expired,
}

/// <summary>An in-flight login: what the callback needs to finish it.</summary>
/// <param name="Verifier">The PKCE code verifier. Secret.</param>
/// <param name="ReturnTo">Where to send the browser once sign-in completes.</param>
public readonly record struct LoginFlow(string Verifier, string ReturnTo)
{
    /// <summary>
    /// Redacts <see cref="Verifier"/>. The compiler-generated <c>ToString</c> prints every
    /// field, so a consumer structured-logging the flow — or putting it in an exception
    /// message — writes the PKCE verifier into logs, where it outlives the flow it protects.
    /// </summary>
    public override string ToString() => $"LoginFlow {{ Verifier = (redacted), ReturnTo = {ReturnTo} }}";
}

/// <summary>The result of consuming a flow: an outcome, and the flow only when there is one.</summary>
/// <remarks>
/// Nullable rather than a fabricated value: a store forced to invent a <see cref="LoginFlow"/>
/// for the three non-<see cref="FlowTake.Taken"/> cases tends to return the real one, handing a
/// used flow's PKCE verifier to the callback path. Construct through the factories below,
/// which are the only way to get a consistent pair.
/// </remarks>
public readonly struct FlowTakeResult : IEquatable<FlowTakeResult>
{
    // Private, so the factories below are not merely the recommended way to build
    // one -- they are the only way. A positional record struct would have kept a
    // public constructor able to pair AlreadyUsed with a real flow, which is the
    // pairing this type exists to make unrepresentable.
    private FlowTakeResult(FlowTake outcome, LoginFlow? flow)
    {
        Outcome = outcome;
        Flow = flow;
    }

    /// <summary>What the store found.</summary>
    public FlowTake Outcome { get; }

    /// <summary>The consumed flow, non-null if and only if <see cref="Outcome"/> is <see cref="FlowTake.Taken"/>.</summary>
    public LoginFlow? Flow { get; }

    /// <summary>This caller consumed <paramref name="flow"/>.</summary>
    public static FlowTakeResult Taken(LoginFlow flow) => new(FlowTake.Taken, flow);

    /// <summary>No such state.</summary>
    public static FlowTakeResult Unknown() => new(FlowTake.Unknown, null);

    /// <summary>The state was already consumed.</summary>
    public static FlowTakeResult AlreadyUsed() => new(FlowTake.AlreadyUsed, null);

    /// <summary>The state was unused but past its lifetime.</summary>
    public static FlowTakeResult Expired() => new(FlowTake.Expired, null);

    /// <inheritdoc/>
    public bool Equals(FlowTakeResult other) =>
        Outcome == other.Outcome && Nullable.Equals(Flow, other.Flow);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FlowTakeResult other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Outcome, Flow);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(FlowTakeResult left, FlowTakeResult right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(FlowTakeResult left, FlowTakeResult right) => !left.Equals(right);
}
