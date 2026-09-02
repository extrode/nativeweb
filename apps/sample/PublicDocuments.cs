using NativeWeb.Render;
using NativeWeb.Web;

namespace NativeWeb.Sample;

/// <summary>
/// The documents a public site answers beside its pages (spec 021 R9): a feed,
/// a sitemap, a robots file and one plain document, each through the
/// framework's format helpers rather than a hand-built string.
/// </summary>
/// <remarks>
/// <para>
/// Every route here is cookie-free and inline-script-free, so each is declared
/// with <c>AsPublicPage</c>: stored in the in-process output cache under its
/// own representation, and labelled <c>public, max-age=60</c> for whatever sits
/// in front. That is the shape <c>/articles</c> cannot take, for the reason in
/// <see cref="PublicArticleEndpoints"/>.
/// </para>
/// <para>
/// Links are written relative and resolved against the request's public base:
/// the scheme <c>UseForwardedHeaders</c> took from the proxy, and the host name
/// the proxy passed through. A feed reader on the far side of that proxy then
/// sees the same absolute URLs a browser does.
/// </para>
/// </remarks>
internal static class PublicDocuments
{
    // The feed shows the same window the page does.
    private const int FeedSize = 20;

    private const string AboutMarkdown =
        "# NativeWeb sample\n" +
        "\n" +
        "The reference application for the *Lean Native Web* architecture: server-rendered HTML " +
        "enhanced with HTMX and Alpine.js, compiled to one native binary.\n" +
        "\n" +
        "## What it answers\n" +
        "\n" +
        "- `/` — the newest published articles, each linking to its page\n" +
        "- `/articles/{slug}` — one article, with its tags\n" +
        "- `/tags` and `/tags/{slug}` — every tag with a count, and one tag's articles\n" +
        "- `/articles` — the published articles as a page, as JSON or as CSV\n" +
        "- `/feed` — the same articles as Atom or RSS\n" +
        "- `/sitemap.xml` and `/robots.txt` — what a crawler expects to find\n" +
        "- `/admin` — the scaffolded editor, behind sign-in\n" +
        "\n" +
        "Every representation above is chosen by the extension on the path or by the " +
        "request's Accept header; this document itself is served as Markdown or as text.\n";

    private const string AboutText =
        "NativeWeb sample\n" +
        "\n" +
        "The reference application for the Lean Native Web architecture: server-rendered HTML " +
        "enhanced with HTMX and Alpine.js, compiled to one native binary.\n" +
        "\n" +
        "What it answers\n" +
        "\n" +
        "  /                          the newest published articles, each linking to its page\n" +
        "  /articles/{slug}           one article, with its tags\n" +
        "  /tags, /tags/{slug}        every tag with a count, and one tag's articles\n" +
        "  /articles                  the published articles as a page, as JSON or as CSV\n" +
        "  /feed                      the same articles as Atom or RSS\n" +
        "  /sitemap.xml, /robots.txt  what a crawler expects to find\n" +
        "  /admin                     the scaffolded editor, behind sign-in\n" +
        "\n" +
        "Every representation above is chosen by the extension on the path or by the " +
        "request's Accept header; this document itself is served as Markdown or as text.\n";

    public static IEndpointRouteBuilder MapPublicDocuments(this IEndpointRouteBuilder endpoints)
    {
        string csp = PublicPageCache.Csp([]);

        // Atom by default, RSS on request: the first offer is what an absent or
        // wildcard Accept header gets.
        endpoints.MapNegotiated("/feed", [Format.Atom, Format.Rss], async (http, format) =>
        {
            IPublishedArticles articles = http.RequestServices.GetRequiredService<IPublishedArticles>();
            IReadOnlyList<PublishedArticle> recent = await articles
                .RecentAsync(FeedSize, http.RequestAborted).ConfigureAwait(false);

            Feed feed = BuildFeed(recent, http.Request.PublicBase());

            return format == Format.Rss
                ? FormatResults.Rss(feed, http.Request)
                : FormatResults.Atom(feed, http.Request);
        })
        .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
        .AsPublicPage(csp);

        // Every published article and every tag, read per render (spec 022 R8);
        // the public cache makes that one read a minute at most.
        endpoints.MapGet("/sitemap.xml", (IPublishedArticles articles, IPublicTags tags, HttpRequest request) =>
            SitemapAsync(articles, tags, request))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
            .AsPublicPage(csp);

        // /version is a framework endpoint that has no business in an index;
        // /admin is behind sign-in and a crawler asking for it only fills the log.
        endpoints.MapGet("/robots.txt", (HttpRequest request) => FormatResults.Text(
            "User-agent: *\n" +
            "Disallow: /admin\n" +
            "Disallow: /version\n" +
            "Sitemap: " + new Uri(request.PublicBase(), "/sitemap.xml").AbsoluteUri + "\n"))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
            .AsPublicPage(csp);

        endpoints.MapNegotiated("/about", [Format.Markdown, Format.Text], static (_, format) =>
            ValueTask.FromResult(format == Format.Text
                ? FormatResults.Text(AboutText)
                : FormatResults.Markdown(AboutMarkdown)))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
            .AsPublicPage(csp);

        return endpoints;
    }

    private static async Task<IResult> SitemapAsync(IPublishedArticles articles, IPublicTags tags, HttpRequest request)
    {
        IReadOnlyList<SitemapArticle> published = await articles.SitemapAsync(request.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        IReadOnlyList<TagSummary> all = await tags.ListAsync(request.HttpContext.RequestAborted).ConfigureAwait(false);

        return FormatResults.Sitemap(SitemapEntries(published, all), request);
    }

    // The pages a crawler should index: the five fixed addresses, then a page
    // per tag and a page per published article, its creation time as lastmod.
    // /articles.json and /feed are data, not pages; /feed.atom is listed because
    // feed discovery is what a sitemap is for and the extension form is the
    // stable address. Every published row is listed, which stays under the
    // format's 50,000-entry ceiling for any site this sample stands in for; a
    // larger one needs a sitemap index, noted in work/todo.md.
    internal static List<SitemapEntry> SitemapEntries(
        IReadOnlyList<SitemapArticle> articles,
        IReadOnlyList<TagSummary> tags)
    {
        List<SitemapEntry> entries =
        [
            new SitemapEntry("/", Frequency: ChangeFrequency.Daily, Priority: 1.0),
            new SitemapEntry("/articles", Frequency: ChangeFrequency.Daily, Priority: 0.8),
            new SitemapEntry("/tags", Frequency: ChangeFrequency.Weekly, Priority: 0.5),
            new SitemapEntry("/feed.atom", Frequency: ChangeFrequency.Daily),
            new SitemapEntry("/about", Frequency: ChangeFrequency.Yearly, Priority: 0.3),
        ];

        foreach (TagSummary tag in tags)
        {
            entries.Add(new SitemapEntry("/tags/" + tag.Slug, Frequency: ChangeFrequency.Weekly, Priority: 0.5));
        }

        foreach (SitemapArticle article in articles)
        {
            entries.Add(new SitemapEntry(
                "/articles/" + article.Slug,
                LastModified: new DateTimeOffset(DateTime.SpecifyKind(article.CreatedAt, DateTimeKind.Utc)),
                Priority: 0.7));
        }

        return entries;
    }

    // Identifiers are the absolute URLs, which is what both formats recommend
    // and what stays stable across a rename of the site's title. The feed's
    // Updated is its newest entry's date, or the epoch for an empty feed --
    // a constant, because a moving "now" would make every render a new
    // document to a reader that compares them.
    internal static Feed BuildFeed(IReadOnlyList<PublishedArticle> articles, Uri baseUrl)
    {
        List<FeedEntry> entries = new(articles.Count);

        foreach (PublishedArticle article in articles)
        {
            string link = "/articles/" + article.Slug;
            entries.Add(new FeedEntry(
                Id: new Uri(baseUrl, link).AbsoluteUri,
                Title: article.Title,
                Link: link,
                Updated: new DateTimeOffset(DateTime.SpecifyKind(article.CreatedAt, DateTimeKind.Utc)),
                Author: article.Author));
        }

        // A feed-level author, because an article's author is optional and an
        // Atom feed without one anywhere is invalid; FormatResults.Atom refuses
        // to write it.
        return new Feed(
            Id: new Uri(baseUrl, "/feed").AbsoluteUri,
            Title: "NativeWeb sample: articles",
            Link: "/articles",
            Updated: entries.Count > 0 ? entries[0].Updated : DateTimeOffset.UnixEpoch,
            Entries: entries,
            Author: "NativeWeb sample",
            Subtitle: "The published articles, newest first.");
    }
}
