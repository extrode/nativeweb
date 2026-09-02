using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The pure halves of the public site (spec 022): the body split and the four
/// compiled templates, each rendered with what its endpoint would pass it.
/// </summary>
public sealed class PublicSiteTests
{
    private static DateTime Utc(int day) => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    private static PublishedArticle Listed(string slug, params TagRef[] tags) =>
        new(slug, slug, $"Title {slug}", "Sy", Utc(4), tags);

    [Fact]
    public void AListedRow_IsDatedByItsPublicationDateAndNotByWhenItWasCreated()
    {
        PublishedArticle backdated = new(
            "b", "b", "Title b", "Sy", new DateTime(2026, 9, 2, 7, 10, 0, DateTimeKind.Utc), [],
            new DateOnly(2026, 8, 24));

        string page = Templates.Articles.Results.ToHtmlString(locale: Locales.En, Articles: [backdated]);

        Assert.Contains("datetime=\"2026-08-24\"", page, StringComparison.Ordinal);
        Assert.Contains(">24 Aug 2026<", page, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-09-02", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AListedRowWithNoPublicationDate_FallsBackToItsCreationDay()
    {
        string page = Templates.Articles.Results.ToHtmlString(locale: Locales.En, Articles: [Listed("one")]);

        Assert.Contains("datetime=\"2026-01-04\"", page, StringComparison.Ordinal);
        Assert.Contains(">4 Jan 2026<", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Paragraphs_SplitsOnBlankLinesAndTrimsEachOne()
    {
        IReadOnlyList<string> paragraphs = PublicSite.Paragraphs("first\r\n\r\n  second line\r\nstill second \n\n\nthird");

        Assert.Equal(["first", "second line\nstill second", "third"], paragraphs);
    }

    [Fact]
    public void Paragraphs_OfAnEmptyBody_IsEmpty() =>
        Assert.Empty(PublicSite.Paragraphs("  \n\n "));

    [Fact]
    public void Results_LinksEachArticleToItsPageAndEachTagToItsPage()
    {
        string html = Templates.Articles.Results.ToHtmlString(
            locale: Locales.En,
            Articles: [Listed("lean-native-web", new TagRef("aot", "NativeAOT"), new TagRef("htmx", "HTMX"))]);

        Assert.Contains("""<a href="/articles/lean-native-web">Title lean-native-web</a>""", html, StringComparison.Ordinal);
        Assert.Contains("""<a class="tag" href="/tags/aot">NativeAOT</a>""", html, StringComparison.Ordinal);
        Assert.Contains("""<a class="tag" href="/tags/htmx">HTMX</a>""", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_WithAnUntaggedArticle_RendersNoTagList()
    {
        string html = Templates.Articles.Results.ToHtmlString(locale: Locales.En, Articles: [Listed("plain")]);

        Assert.DoesNotContain("<ul class=\"tags\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_IsAWholeDocumentWithTheListAndNoForm()
    {
        string page = Templates.Site.Home.ToHtmlString(
            locale: Locales.En,
            Title: "Latest articles",
            AntiforgeryToken: "",
            Articles: [Listed("one")]);

        Assert.Contains("<!DOCTYPE html>", page, StringComparison.Ordinal);
        Assert.Contains("<h1 class=\"hero__title\">Server-rendered HTML, one native binary</h1>", page, StringComparison.Ordinal);
        Assert.Contains("<h2 class=\"page__title\">Latest</h2>", page, StringComparison.Ordinal);
        Assert.Contains("/articles/one", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", page, StringComparison.Ordinal);
        Assert.DoesNotContain("request-verification-token", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_BrandLinksHomeAndTheNavReachesTags()
    {
        string page = Templates.Site.Home.ToHtmlString(
            locale: Locales.En, Title: "t", AntiforgeryToken: "", Articles: []);

        Assert.Contains("""<a class="topbar__brand" href="/">""", page, StringComparison.Ordinal);
        Assert.Contains("""<a href="/tags">Tags</a>""", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_RendersTitleMetaTagsAndOneParagraphPerEntry()
    {
        var detail = new ArticleDetail(
            "lean", "Lean Native Web", "Sy", Utc(4), "ignored here", [new TagRef("aot", "NativeAOT")]);

        string page = Templates.Articles.Show.ToHtmlString(
            locale: Locales.En,
            Title: detail.Title,
            AntiforgeryToken: "",
            Article: detail,
            Paragraphs: ["first", "second"]);

        Assert.Contains("<h1 class=\"page__title\">Lean Native Web</h1>", page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"articles__author\">Sy</span>", page, StringComparison.Ordinal);
        Assert.Contains("datetime=\"2026-01-04\"", page, StringComparison.Ordinal);
        Assert.Contains("""<a class="tag" href="/tags/aot">NativeAOT</a>""", page, StringComparison.Ordinal);
        Assert.Contains("<p>first</p>", page, StringComparison.Ordinal);
        Assert.Contains("<p>second</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored here", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_EncodesTheBodyAndOmitsAMissingAuthor()
    {
        var detail = new ArticleDetail("x", "t", null, Utc(4), "", []);

        string page = Templates.Articles.Show.ToHtmlString(
            locale: Locales.En,
            Title: "t",
            AntiforgeryToken: "",
            Article: detail,
            Paragraphs: ["<script>alert(1)</script>"]);

        Assert.DoesNotContain("<script>", page, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("articles__author", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TagsIndex_ListsEachTagWithItsCountAndLink()
    {
        string page = Templates.Tags.Index.ToHtmlString(
            locale: Locales.En,
            Title: "Tags",
            AntiforgeryToken: "",
            Tags: [new TagSummary("aot", "NativeAOT", 2), new TagSummary("postgres", "PostgreSQL", 0)]);

        Assert.Contains("""<a class="tag" href="/tags/aot">NativeAOT</a>""", page, StringComparison.Ordinal);
        Assert.Contains(">2</span>", page, StringComparison.Ordinal);
        Assert.Contains(">0</span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No tags yet.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TagsIndex_WithNoTags_SaysSo()
    {
        string page = Templates.Tags.Index.ToHtmlString(
            locale: Locales.En, Title: "Tags", AntiforgeryToken: "", Tags: []);

        Assert.Contains("No tags yet.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TagShow_HeadsWithTheNameAndSaysWhenThereAreNoArticles()
    {
        string page = Templates.Tags.Show.ToHtmlString(
            locale: Locales.En, Title: "PostgreSQL", AntiforgeryToken: "", Name: "PostgreSQL", Articles: []);

        Assert.Contains("<h1 class=\"page__title\">PostgreSQL</h1>", page, StringComparison.Ordinal);
        Assert.Contains("No published articles yet.", page, StringComparison.Ordinal);
    }
}
