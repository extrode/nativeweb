using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// Spec 027 R6 and R7. Against a real ephemeral key ring rather than a stub protector:
/// the properties under test are that a changed byte fails to authenticate and that a
/// lifetime is honoured, and a stub that returns its input would assert neither.
/// </summary>
public sealed class CarriedAnswersTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    [Fact]
    public void Answers_round_trip()
    {
        CarriedAnswers carried = Helper(out _);

        string field = carried.Protect(
            new Dictionary<string, string> { ["name"] = "Ada", ["role"] = "engineer" }, TenMinutes);

        Assert.True(carried.TryUnprotect(field, out IReadOnlyDictionary<string, string> back, out CarriedAnswersFailure failure));
        Assert.Equal(CarriedAnswersFailure.None, failure);
        Assert.Equal("Ada", back["name"]);
        Assert.Equal("engineer", back["role"]);
        Assert.Equal(2, back.Count);
    }

    [Fact]
    public void An_empty_set_round_trips()
    {
        CarriedAnswers carried = Helper(out _);

        string field = carried.Protect(new Dictionary<string, string>(), TenMinutes);

        Assert.True(carried.TryUnprotect(field, out IReadOnlyDictionary<string, string> back, out _));
        Assert.Empty(back);
    }

    /// <summary>
    /// The payload is length-prefixed so that no value needs escaping. A
    /// delimiter-joined format would get this wrong once, on the answer that happened
    /// to contain a delimiter.
    /// </summary>
    [Theory]
    [InlineData("5:not a length")]
    [InlineData("a:b:c")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("contains\na newline")]
    public void A_value_containing_the_payloads_own_delimiters_round_trips(string awkward)
    {
        CarriedAnswers carried = Helper(out _);

        string field = carried.Protect(new Dictionary<string, string> { ["answer"] = awkward }, TenMinutes);

        Assert.True(carried.TryUnprotect(field, out IReadOnlyDictionary<string, string> back, out _));
        Assert.Equal(awkward, back["answer"]);
    }

    [Fact]
    public void A_key_containing_the_payloads_own_delimiters_round_trips()
    {
        CarriedAnswers carried = Helper(out _);

        string field = carried.Protect(new Dictionary<string, string> { ["3:odd"] = "value" }, TenMinutes);

        Assert.True(carried.TryUnprotect(field, out IReadOnlyDictionary<string, string> back, out _));
        Assert.Equal("value", back["3:odd"]);
    }

    [Fact]
    public void One_changed_byte_is_altered_and_yields_nothing()
    {
        CarriedAnswers carried = Helper(out _);
        string field = carried.Protect(new Dictionary<string, string> { ["name"] = "Ada" }, TenMinutes);

        string tampered = Flip(field);

        Assert.False(carried.TryUnprotect(tampered, out IReadOnlyDictionary<string, string> back, out CarriedAnswersFailure failure));
        Assert.Equal(CarriedAnswersFailure.Altered, failure);
        Assert.Empty(back);
    }

    [Fact]
    public void A_value_past_its_lifetime_is_expired_and_not_altered()
    {
        CarriedAnswers carried = Helper(out MovableClock clock);
        string field = carried.Protect(new Dictionary<string, string> { ["name"] = "Ada" }, TenMinutes);

        clock.Advance(TenMinutes + TimeSpan.FromSeconds(1));

        Assert.False(carried.TryUnprotect(field, out _, out CarriedAnswersFailure failure));
        Assert.Equal(CarriedAnswersFailure.Expired, failure);
    }

    [Fact]
    public void A_value_inside_its_lifetime_still_reads()
    {
        CarriedAnswers carried = Helper(out MovableClock clock);
        string field = carried.Protect(new Dictionary<string, string> { ["name"] = "Ada" }, TenMinutes);

        clock.Advance(TenMinutes - TimeSpan.FromSeconds(1));

        Assert.True(carried.TryUnprotect(field, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_field_is_absent_and_not_altered(string? field)
    {
        Assert.False(Helper(out _).TryUnprotect(field, out _, out CarriedAnswersFailure failure));
        Assert.Equal(CarriedAnswersFailure.Absent, failure);
    }

    /// <summary>
    /// Two hosts, two key rings. A value made by one must not read on the other, or a
    /// hidden field would be portable between deployments.
    /// </summary>
    [Fact]
    public void A_value_from_another_key_ring_is_altered()
    {
        string field = Helper(out _).Protect(new Dictionary<string, string> { ["name"] = "Ada" }, TenMinutes);

        Assert.False(Helper(out _).TryUnprotect(field, out _, out CarriedAnswersFailure failure));
        Assert.Equal(CarriedAnswersFailure.Altered, failure);
    }

    /// <summary>
    /// The purpose string is what stops a value made for one use being replayed into
    /// another on the same host, with the same keys.
    /// </summary>
    [Fact]
    public void A_value_protected_for_another_purpose_is_altered()
    {
        IDataProtectionProvider provider = Provider();
        var carried = new CarriedAnswers(provider);
        string elsewhere = provider.CreateProtector("something.else").Protect("7:0000000004name3:Ada");

        Assert.False(carried.TryUnprotect(elsewhere, out _, out CarriedAnswersFailure failure));
        Assert.Equal(CarriedAnswersFailure.Altered, failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_lifetime_of_zero_or_less_is_refused(int minutes)
    {
        CarriedAnswers carried = Helper(out _);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => carried.Protect(new Dictionary<string, string>(), TimeSpan.FromMinutes(minutes)));
    }

    private static CarriedAnswers Helper(out MovableClock clock)
    {
        clock = new MovableClock(DateTimeOffset.UnixEpoch.AddYears(56));
        return new CarriedAnswers(Provider(), clock);
    }

    // The repo writes its own rather than taking Microsoft.Extensions.TimeProvider.Testing:
    // five other tiers already do, and a package for one movable clock is a dependency
    // the build would carry for a type that is nine lines.
    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        internal void Advance(TimeSpan by) => _now = _now.Add(by);

        public override DateTimeOffset GetUtcNow() => _now;
    }

    // Ephemeral, and that is not a detail. A bare AddDataProtection() persists its key
    // ring to the user profile, so two providers built that way share keys -- which
    // would silently turn the cross-key-ring fact below into a test of nothing, and
    // would have this suite writing keys onto whatever machine ran it.
    private static IDataProtectionProvider Provider() =>
        new ServiceCollection()
            .AddDataProtection()
            .UseEphemeralDataProtectionProvider()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

    // Base64url alphabet, so the flipped character keeps the value decodable and the
    // rejection comes from authentication rather than from a malformed payload.
    private static string Flip(string field)
    {
        char[] characters = field.ToCharArray();
        int at = characters.Length / 2;
        characters[at] = characters[at] == 'A' ? 'B' : 'A';
        return new string(characters);
    }
}
