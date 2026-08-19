using System.Globalization;

using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// The converters the generated binder calls. Culture is the point of most of
/// these: a form field is a wire format, and parsing one with the ambient culture
/// turns 1.5 into 15 in half of Europe.
/// </summary>
public sealed class ScaffoldValuesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  hi  ", "hi")]
    public void Text_TreatsBlankAsAbsent(string? raw, string? expected) =>
        Assert.Equal(expected, ScaffoldValues.Text(raw));

    [Theory]
    [InlineData("42", 42)]
    [InlineData(" 42 ", 42)]
    [InlineData("-7", -7)]
    [InlineData("not a number", 0)]
    [InlineData(null, 0)]
    public void Int32_FallsBackRatherThanThrowing(string? raw, int expected) =>
        Assert.Equal(expected, ScaffoldValues.Int32(raw));

    [Fact]
    public void Int32_HonoursTheSuppliedFallback() =>
        Assert.Equal(9, ScaffoldValues.Int32("rubbish", 9));

    [Theory]
    [InlineData("42", 42)]
    [InlineData("", null)]
    [InlineData("rubbish", null)]
    public void NullableInt32_DistinguishesAbsentFromZero(string? raw, int? expected) =>
        Assert.Equal(expected, ScaffoldValues.NullableInt32(raw));

    [Fact]
    public void Int64_ParsesBeyondInt32Range() =>
        Assert.Equal(9_000_000_000L, ScaffoldValues.Int64("9000000000"));

    [Fact]
    public void NullableInt64_ReturnsNullForRubbish() =>
        Assert.Null(ScaffoldValues.NullableInt64("x"));

    // The regression this file exists for: where "." groups and "," separates the
    // decimal, "1.5" read with the ambient culture is one thousand five hundred.
    //
    // Built by hand rather than by naming de-DE, because InvariantGlobalization is
    // on repo-wide and new CultureInfo("de-DE") throws. A cloned invariant culture
    // with the separators swapped reproduces the same hazard with no ICU data.
    [Fact]
    public void Decimal_IsInvariantRegardlessOfAmbientCulture()
    {
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.NumberFormat.NumberDecimalSeparator = ",";
        hostile.NumberFormat.NumberGroupSeparator = ".";

        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = hostile;

            // The hazard is real under this culture...
            Assert.Equal(15m, decimal.Parse("1.5", NumberStyles.Number, CultureInfo.CurrentCulture));

            // ...and the converters are immune to it.
            Assert.Equal(1.5m, ScaffoldValues.Decimal("1.5"));
            Assert.Equal(1.5d, ScaffoldValues.Double("1.5"));
            Assert.Equal(1.5m, ScaffoldValues.NullableDecimal("1.5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NullableDecimal_ReturnsNullForBlank() =>
        Assert.Null(ScaffoldValues.NullableDecimal("  "));

    // An unticked checkbox submits nothing at all, so absence must read as false
    // rather than as "unchanged" -- otherwise a box can be ticked but never cleared.
    [Theory]
    [InlineData("on", true)]
    [InlineData("ON", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("off", false)]
    public void Boolean_AbsenceIsFalse(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.Boolean(raw));

    [Fact]
    public void DateTimeValue_AssumesUtcForAnUnzonedFormValue()
    {
        DateTime parsed = ScaffoldValues.DateTimeValue("2026-03-04T09:30", DateTime.UnixEpoch);

        Assert.Equal(new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    [Fact]
    public void DateTimeValue_FallsBackWhenUnparseable() =>
        Assert.Equal(DateTime.UnixEpoch, ScaffoldValues.DateTimeValue("nonsense", DateTime.UnixEpoch));

    [Fact]
    public void NullableDateTime_ReturnsNullForBlank() =>
        Assert.Null(ScaffoldValues.NullableDateTime(""));

    [Fact]
    public void GuidValue_FallsBackToEmpty() =>
        Assert.Equal(Guid.Empty, ScaffoldValues.GuidValue("not-a-guid"));

    [Theory]
    [InlineData(null, "")]
    [InlineData(true, "Yes")]
    [InlineData(false, "No")]
    [InlineData(42, "42")]
    [InlineData("plain", "plain")]
    public void Display_FormatsForAListCell(object? value, string expected) =>
        Assert.Equal(expected, ScaffoldValues.Display(value));

    [Fact]
    public void Display_FormatsDatesReadably() =>
        Assert.Equal("2026-03-04 09:30", ScaffoldValues.Display(new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc)));

    // datetime-local will not accept the display form, so Editable is not Display.
    [Fact]
    public void Editable_UsesTheDatetimeLocalWireFormat() =>
        Assert.Equal("2026-03-04T09:30", ScaffoldValues.Editable(new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void Editable_RendersBooleansAsCheckboxValues()
    {
        Assert.Equal("on", ScaffoldValues.Editable(true));
        Assert.Equal("", ScaffoldValues.Editable(false));
    }

    [Fact]
    public void Editable_RoundTripsThroughBoolean() =>
        Assert.True(ScaffoldValues.Boolean(ScaffoldValues.Editable(true)));

    [Fact]
    public void RequiresAntiforgery_AlwaysValidates() =>
        Assert.True(RequiresAntiforgery.Instance.RequiresValidation);
}
