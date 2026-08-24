using NativeWeb.Sample;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// <see cref="JauntyArticleStore.PatternFor"/>, which is the one piece of the
/// JauntyQ store that is pure enough to test without a database.
/// </summary>
/// <remarks>
/// It is also the piece most worth testing. The scaffolded search box posts
/// whatever the user typed straight into an ILIKE pattern, and ILIKE gives
/// <c>%</c> and <c>_</c> meaning. Unescaped, a user searching for a literal
/// percent sign gets every row in the table back and no error to explain it. The
/// integration tier proves the escaping works against real PostgreSQL; this tier
/// pins the exact string, so a regression names the character that broke.
/// </remarks>
public sealed class SearchPatternTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PatternFor_WithNothingToSearchFor_MatchesEverything(string? search)
    {
        // '%' rather than an empty pattern: the queries have no unfiltered branch,
        // so "no search" has to be expressed as a pattern that matches every row.
        Assert.Equal("%", JauntyArticleStore.PatternFor(search));
    }

    [Fact]
    public void PatternFor_WithAPlainTerm_WrapsItUnanchored()
    {
        Assert.Equal("%native%", JauntyArticleStore.PatternFor("native"));
    }

    [Fact]
    public void PatternFor_WithAPercent_EscapesIt()
    {
        // Without this, searching "50%" returns the whole table.
        Assert.Equal(@"%50\%%", JauntyArticleStore.PatternFor("50%"));
    }

    [Fact]
    public void PatternFor_WithAnUnderscore_EscapesIt()
    {
        // Without this, "view_count" also matches "viewXcount".
        Assert.Equal(@"%view\_count%", JauntyArticleStore.PatternFor("view_count"));
    }

    [Fact]
    public void PatternFor_WithATrailingBackslash_EscapesIt()
    {
        // A pattern ending in a lone backslash is malformed and PostgreSQL raises
        // rather than returning nothing, so this one is a 500 rather than a wrong
        // answer -- which is worse, and reachable from a text box.
        Assert.Equal(@"%c:\\%", JauntyArticleStore.PatternFor(@"c:\"));
    }

    /// <summary>
    /// The ordering fact, which is the one a rewrite is most likely to break.
    /// </summary>
    /// <remarks>
    /// Backslash must be escaped first. Do it last and the backslashes introduced
    /// while escaping <c>%</c> and <c>_</c> get escaped a second time, so the
    /// pattern searches for a literal backslash the user never typed and the row
    /// they wanted is not found. Both orders produce plausible-looking output; only
    /// this input tells them apart.
    /// </remarks>
    [Fact]
    public void PatternFor_EscapesTheBackslashBeforeTheWildcards()
    {
        // Input is a backslash followed by a percent. Correct: both escaped once.
        // Escaping backslash last would give %\\\\%% instead.
        Assert.Equal(@"%\\\%%", JauntyArticleStore.PatternFor(@"\%"));
    }
}
