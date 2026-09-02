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
/// Links are written relative and resolved against the request's public base,
/// which <c>UseForwardedHeaders</c> has already corrected to the scheme and host
/// the visitor used. A feed reader on the far side of a proxy then sees the
/// same absolute URLs a browser does.
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

        endpoints.MapGet("/sitemap.xml", (HttpRequest request) => FormatResults.Sitemap(SitemapEntries, request))
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

    // The three routes a crawler should index. /articles.json and /feed are
    // data, not pages; /feed.atom is listed because feed discovery is what a
    // sitemap is for and the extension form is the stable address.
    private static readonly SitemapEntry[] SitemapEntries =
    [
        new SitemapEntry("/articles", Frequency: ChangeFrequency.Daily, Priority: 1.0),
        new SitemapEntry("/feed.atom", Frequency: ChangeFrequency.Daily),
        new SitemapEntry("/about", Frequency: ChangeFrequency.Yearly, Priority: 0.3),
    ];

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
            string link = "/articles#" + article.Id;
            entries.Add(new FeedEntry(
                Id: new Uri(baseUrl, link).AbsoluteUri,
                Title: article.Title,
                Link: link,
                Updated: new DateTimeOffset(DateTime.SpecifyKind(article.CreatedAt, DateTimeKind.Utc)),
                Author: article.Author));
        }

        return new Feed(
            Id: new Uri(baseUrl, "/feed").AbsoluteUri,
            Title: "NativeWeb sample: articles",
            Link: "/articles",
            Updated: entries.Count > 0 ? entries[0].Updated : DateTimeOffset.UnixEpoch,
            Entries: entries,
            Subtitle: "The published articles, newest first.");
    }
}
