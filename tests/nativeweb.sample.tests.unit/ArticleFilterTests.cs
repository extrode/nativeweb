using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The two pure helpers behind <c>POST /articles</c>, the route spec 014 converted to
/// a declarative form model.
/// </summary>
/// <remarks>
/// <para>
/// The form model binds strings and nothing else — every member of
/// <see cref="ArticleFilter"/> is a <c>string?</c>, by design, so a request that says
/// <c>max=banana</c> is a rendering question rather than a binding failure. That puts
/// both decisions here, in ordinary code that can be tested one rule at a time:
/// <b>what counts as a match</b>, and <b>what an unusable page size becomes</b>.
/// </para>
/// <para>
/// The integration tier can only say the page came back with two articles on it. Only
/// here can "unparseable falls back to the default" and "the ceiling holds" be stated
/// separately, so a failure names the rule that broke.
/// </para>
/// </remarks>
public sealed class ArticleFilterTests
{
    private static PublishedArticle Article(string title) =>
        new(title, title, title, "Ada", new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc), []);

    /// <summary>
    /// A blank box means "everything". Treating it as a filter that matches nothing
    /// would make the first load of the page empty.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_is_not_a_filter(string? query)
    {
        IReadOnlyList<PublishedArticle> all = [Article("Alpha"), Article("Beta")];

        Assert.Same(all, PublicArticleEndpoints.Matching(all, query));
    }

    [Fact]
    public void A_query_keeps_only_the_titles_that_contain_it()
    {
        IReadOnlyList<PublishedArticle> matching = PublicArticleEndpoints.Matching(
            [Article("Alpha"), Article("Beta"), Article("Alphabet")],
            "alpha");

        Assert.Equal(["Alpha", "Alphabet"], matching.Select(a => a.Title));
    }

    /// <summary>
    /// Case-insensitive, because a search box that makes the user match the author's
    /// capitalisation is a search box nobody uses.
    /// </summary>
    [Theory]
    [InlineData("ALPHA")]
    [InlineData("alpha")]
    [InlineData("AlPhA")]
    public void The_query_is_case_insensitive(string query)
    {
        Assert.Single(PublicArticleEndpoints.Matching([Article("Alpha"), Article("Beta")], query));
    }

    [Fact]
    public void A_query_that_matches_nothing_yields_nothing()
    {
        Assert.Empty(PublicArticleEndpoints.Matching([Article("Alpha")], "zeta"));
    }

    /// <summary>
    /// Substring, not prefix — the box is a filter over what is on the page, not a
    /// lookup.
    /// </summary>
    [Fact]
    public void The_query_matches_anywhere_in_the_title()
    {
        Assert.Single(PublicArticleEndpoints.Matching([Article("The Alpha Release")], "Alpha Rel"));
    }

    [Fact]
    public void A_page_size_is_taken_as_written()
    {
        Assert.Equal(5, PublicArticleEndpoints.ParseTake("5"));
    }

    /// <summary>
    /// Anything unusable is the default rather than a 400: a page size is not worth
    /// failing a request over, and the model has already accepted the string.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("banana")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public void An_unusable_page_size_falls_back_to_the_default(string? raw)
    {
        Assert.Equal(20, PublicArticleEndpoints.ParseTake(raw));
    }

    /// <summary>
    /// <b>The ceiling is the guard, not a nicety.</b> Without it the page size is a
    /// user-supplied row count, which is a denial-of-service lever pointed at the
    /// database.
    /// </summary>
    [Theory]
    [InlineData("100", 100)]
    [InlineData("101", 100)]
    [InlineData("100000", 100)]
    public void The_page_size_is_capped(string raw, int expected)
    {
        Assert.Equal(expected, PublicArticleEndpoints.ParseTake(raw));
    }
}
