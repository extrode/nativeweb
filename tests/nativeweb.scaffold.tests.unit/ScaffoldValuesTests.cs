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
    public void NullableDateTime_AssumesUtcForAnUnzonedFormValue()
    {
        DateTime? parsed = ScaffoldValues.NullableDateTime("2026-03-04T09:30");

        Assert.Equal(new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Fact]
    public void NullableDateTime_ConvertsAnOffsetToUtc()
    {
        DateTime? parsed = ScaffoldValues.NullableDateTime("2026-03-04T09:30:00+02:00");

        Assert.Equal(new DateTime(2026, 3, 4, 7, 30, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Fact]
    public void DateTimeValue_ConvertsAnOffsetToUtc()
    {
        DateTime parsed = ScaffoldValues.DateTimeValue("2026-03-04T09:30:00+02:00", DateTime.UnixEpoch);

        Assert.Equal(new DateTime(2026, 3, 4, 7, 30, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

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

    // The four converters that did not exist, on which the emitter fell through to
    // the string case and generated `short x = "abc" ?? ""` -- CS0029, on a model
    // that had declared nothing unusual.
    [Fact]
    public void Int16_ParsesAndFallsBack()
    {
        Assert.Equal((short)42, ScaffoldValues.Int16("42"));
        Assert.Equal((short)0, ScaffoldValues.Int16("70000"));
        Assert.Null(ScaffoldValues.NullableInt16("70000"));
        Assert.Equal((short)-7, ScaffoldValues.NullableInt16("-7"));
    }

    [Fact]
    public void Single_IsInvariantAndFallsBack()
    {
        Assert.Equal(1.5f, ScaffoldValues.Single("1.5"));
        Assert.Equal(0f, ScaffoldValues.Single("x"));
        Assert.Null(ScaffoldValues.NullableSingle(""));
        Assert.Equal(2.25f, ScaffoldValues.NullableSingle("2.25"));
    }

    [Fact]
    public void NullableDouble_DistinguishesAbsentFromZero()
    {
        Assert.Null(ScaffoldValues.NullableDouble(""));
        Assert.Null(ScaffoldValues.NullableDouble("rubbish"));
        Assert.Equal(0d, ScaffoldValues.NullableDouble("0"));
    }

    [Fact]
    public void NullableGuid_DistinguishesAbsentFromEmpty()
    {
        Assert.Null(ScaffoldValues.NullableGuid(""));
        Assert.Null(ScaffoldValues.NullableGuid("not-a-guid"));

        // The distinction the nullable form exists for: GuidValue cannot tell "the
        // field was left blank" from "the user typed the all-zero guid".
        Assert.Equal(Guid.Empty, ScaffoldValues.NullableGuid(Guid.Empty.ToString()));
        Assert.Equal(Guid.Empty, ScaffoldValues.GuidValue(""));
    }

    // The validators. Blank is valid -- an absent field takes the documented
    // fallback -- and a submitted value that cannot be read as its type is not.
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("42", true)]
    [InlineData(" -7 ", true)]
    [InlineData("4.2", false)]
    [InlineData("abc", false)]
    [InlineData("9999999999", false)]
    public void IsInt32_AcceptsBlankAndRejectsMalformed(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.IsInt32(raw));

    [Theory]
    [InlineData("", true)]
    [InlineData("42", true)]
    [InlineData("70000", false)]
    public void IsInt16_IsBoundedByTheType(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.IsInt16(raw));

    [Theory]
    [InlineData("", true)]
    [InlineData("9000000000", true)]
    [InlineData("x", false)]
    public void IsInt64_AcceptsWhatInt64Parses(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.IsInt64(raw));

    [Theory]
    [InlineData("", true)]
    [InlineData("1.5", true)]
    [InlineData("abc", false)]
    public void IsDecimalAndFriends_AgreeOnTheOrdinaryCases(string? raw, bool expected)
    {
        Assert.Equal(expected, ScaffoldValues.IsDecimal(raw));
        Assert.Equal(expected, ScaffoldValues.IsDouble(raw));
        Assert.Equal(expected, ScaffoldValues.IsSingle(raw));
    }

    // Where they do NOT agree, and each validator matches its own converter, which
    // is the property that matters: a value the validator accepts must be one the
    // converter can read.
    //
    // Decimal parses with NumberStyles.Number, which allows a group separator, so
    // "1,5" is one thousand five hundred to it. Double and Single parse with
    // NumberStyles.Float, which does not, so the same string is malformed. That
    // asymmetry predates this batch and is noted in work/todo.md rather than
    // changed here -- silently narrowing Decimal would reject strings that have
    // been accepted and stored since the converter was written.
    [Fact]
    public void AGroupSeparator_IsDecimalOnlyAndTheValidatorsSayTheSame()
    {
        Assert.True(ScaffoldValues.IsDecimal("1,5"));
        Assert.Equal(15m, ScaffoldValues.Decimal("1,5"));

        Assert.False(ScaffoldValues.IsDouble("1,5"));
        Assert.False(ScaffoldValues.IsSingle("1,5"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("2026-03-04T09:30", true)]
    [InlineData("2026-13-04", false)]
    [InlineData("tomorrow", false)]
    public void IsDateTime_RejectsWhatWouldHaveBecomeNow(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.IsDateTime(raw));

    [Theory]
    [InlineData("", true)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e", true)]
    [InlineData("xyz", false)]
    public void IsGuid_RejectsWhatWouldHaveBecomeEmpty(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.IsGuid(raw));

    private static readonly string[] Sortable = ["Title", "Author", "Id"];

    [Theory]
    [InlineData("Title", "Title")]
    [InlineData("Author", "Author")]
    [InlineData("Body", "Id")]
    [InlineData("title", "Id")]
    [InlineData("", "Id")]
    [InlineData(null, "Id")]
    [InlineData("id; drop table articles", "Id")]
    public void Sort_AcceptsOnlyAnAllowListedProperty(string? requested, string expected) =>
        Assert.Equal(expected, ScaffoldValues.Sort(requested, null, Sortable, "Id").Property);

    [Theory]
    [InlineData("desc", ScaffoldSortDirection.Descending)]
    [InlineData("DESC", ScaffoldSortDirection.Descending)]
    [InlineData("asc", ScaffoldSortDirection.Ascending)]
    [InlineData("sideways", ScaffoldSortDirection.Ascending)]
    [InlineData(null, ScaffoldSortDirection.Ascending)]
    public void Sort_ReadsTheDirectionAndDefaultsToAscending(string? raw, ScaffoldSortDirection expected) =>
        Assert.Equal(expected, ScaffoldValues.Sort("Title", raw, Sortable, "Id").Direction);

    [Theory]
    [InlineData("Body")]
    [InlineData("id; drop table articles")]
    public void Sort_TakesTheDirectionDownWithAnUnrecognisedProperty(string requested) =>
        Assert.Equal(
            ScaffoldSortDirection.Ascending,
            ScaffoldValues.Sort(requested, "desc", Sortable, "Id").Direction);

    [Fact]
    public void Sort_KeepsTheDirectionWhenNoPropertyWasNamed() =>
        Assert.Equal(
            ScaffoldSortDirection.Descending,
            ScaffoldValues.Sort(null, "desc", Sortable, "Id").Direction);

    [Fact]
    public void Cursor_ReturnsOneIssuedForTheOrderingInForce()
    {
        var sort = new ScaffoldSortSpec("Title", ScaffoldSortDirection.Descending);
        string token = new ScaffoldCursor("Title", ScaffoldSortDirection.Descending, "Meditations", "a1", false).Encode();

        Assert.Equal("a1", ScaffoldValues.Cursor(token, sort)?.Key);
    }

    [Fact]
    public void Cursor_DropsOneIssuedForAnotherOrdering()
    {
        string token = new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "Meditations", "a1", false).Encode();

        Assert.Null(ScaffoldValues.Cursor(token, new ScaffoldSortSpec("Author", ScaffoldSortDirection.Ascending)));
        Assert.Null(ScaffoldValues.Cursor(token, new ScaffoldSortSpec("Title", ScaffoldSortDirection.Descending)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mangled")]
    public void Cursor_IsNullForAnythingUnreadable(string? raw) =>
        Assert.Null(ScaffoldValues.Cursor(raw, new ScaffoldSortSpec("Title", ScaffoldSortDirection.Ascending)));

    [Fact]
    public void CursorText_KeepsWhatDisplayRoundsAway()
    {
        var when = new DateTime(2026, 3, 4, 9, 30, 27, 500, DateTimeKind.Utc);

        Assert.Equal("2026-03-04T09:30:27.5000000Z", ScaffoldValues.CursorText(when));
        Assert.NotEqual(ScaffoldValues.Display(when), ScaffoldValues.CursorText(when));
    }

    [Fact]
    public void CursorText_IsInvariantAndNullPreserving()
    {
        Assert.Null(ScaffoldValues.CursorText(null));
        Assert.Equal("", ScaffoldValues.CursorText(""));
        Assert.Equal("1.5", ScaffoldValues.CursorText(1.5m));
        Assert.Equal("1", ScaffoldValues.CursorText(true));
        Assert.Equal("0", ScaffoldValues.CursorText(false));
    }

    private enum Status
    {
        Draft,
        InReview,
        Published,
    }

    [Theory]
    [InlineData("2026-03-04", true)]
    [InlineData(" 2026-03-04 ", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("2026-02-30", false)]
    [InlineData("3/4/2026", false)]
    [InlineData("2026-03-04T09:30", false)]
    [InlineData("tomorrow", false)]
    public void IsDateOnly_AcceptsIsoDatesAndBlank(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.IsDateOnly(raw));

    [Fact]
    public void DateOnlyValue_ParsesIsoAndFallsBackOtherwise()
    {
        var fallback = new DateOnly(2000, 1, 1);

        Assert.Equal(new DateOnly(2026, 3, 4), ScaffoldValues.DateOnlyValue("2026-03-04", fallback));
        Assert.Equal(fallback, ScaffoldValues.DateOnlyValue("2026-02-30", fallback));
        Assert.Equal(fallback, ScaffoldValues.DateOnlyValue(null, fallback));
    }

    [Fact]
    public void NullableDateOnly_DistinguishesBlankFromMalformed()
    {
        Assert.Equal(new DateOnly(2026, 3, 4), ScaffoldValues.NullableDateOnly("2026-03-04"));
        Assert.Null(ScaffoldValues.NullableDateOnly(""));
        Assert.Null(ScaffoldValues.NullableDateOnly("2026-02-30"));
    }

    [Theory]
    [InlineData("Published", true)]
    [InlineData(" InReview ", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("published", false)]
    [InlineData("2", false)]
    [InlineData("Deleted", false)]
    [InlineData("In Review", false)]
    public void IsEnumMember_TakesTheExactMemberNameOrBlank(string? raw, bool expected) =>
        Assert.Equal(expected, ScaffoldValues.IsEnumMember<Status>(raw));

    [Fact]
    public void EnumValue_ParsesAMemberAndFallsBackOnAnythingElse()
    {
        Assert.Equal(Status.Published, ScaffoldValues.EnumValue("Published", Status.Draft));
        Assert.Equal(Status.Draft, ScaffoldValues.EnumValue("2", Status.Draft));
        Assert.Equal(Status.Draft, ScaffoldValues.EnumValue("Deleted", Status.Draft));
        Assert.Equal(Status.Draft, ScaffoldValues.EnumValue(null, Status.Draft));
    }

    [Fact]
    public void NullableEnum_IsNullForBlankAndForANonMember()
    {
        Assert.Equal(Status.InReview, ScaffoldValues.NullableEnum<Status>("InReview"));
        Assert.Null(ScaffoldValues.NullableEnum<Status>(""));
        Assert.Null(ScaffoldValues.NullableEnum<Status>("1"));
    }

    [Theory]
    [InlineData("Draft", "Draft")]
    [InlineData("InReview", "In Review")]
    [InlineData("NativeAOT", "Native AOT")]
    [InlineData("A", "A")]
    public void EnumLabel_SpacesAtEachCaseChange(string member, string expected) =>
        Assert.Equal(expected, ScaffoldValues.EnumLabel(member));

    [Fact]
    public void Display_LabelsAnEnumAndFormatsADate()
    {
        Assert.Equal("In Review", ScaffoldValues.Display(Status.InReview));
        Assert.Equal("2026-03-04", ScaffoldValues.Display(new DateOnly(2026, 3, 4)));
    }

    [Fact]
    public void Editable_KeepsTheMemberNameForTheOptionValue()
    {
        Assert.Equal("InReview", ScaffoldValues.Editable(Status.InReview));
        Assert.Equal("2026-03-04", ScaffoldValues.Editable(new DateOnly(2026, 3, 4)));
    }

    [Fact]
    public void Keys_TrimsDropsBlanksAndDuplicatesAndKeepsOrder()
    {
        Assert.Equal(["htmx", "aot"], ScaffoldValues.Keys([" htmx ", "", "aot", "htmx", "  "]));
        Assert.Empty(ScaffoldValues.Keys(null));
        Assert.Empty(ScaffoldValues.Keys([]));
    }
}
