namespace NativeWeb.SignIn.Testing;

/// <summary>
/// A movable clock the conformance suites use, so an expiry case does not cost the fixture the
/// wall-clock time it is asserting about.
/// </summary>
/// <remarks>
/// A store under test must read the clock it was handed rather than <see cref="DateTimeOffset.UtcNow"/>.
/// An implementation that ignores it fails <c>TakeAsync_PastTheExpiry_IsRefused</c>, which is the
/// point: a store judging expiry against its own idea of now cannot be tested and cannot be
/// reasoned about under a clock skew either.
/// </remarks>
public sealed class ConformanceClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far. Must not be negative.</param>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        _now += by;
    }
}
