using NativeWeb.Render;
using NativeWeb.Web;

namespace NativeWeb.Sample;

/// <summary>
/// The public site (spec 022): the home page, one page per published article,
/// the tag listing and one page per tag. Hand-written routes over the two read
/// seams, rendered through the compiled templates and the shared layout, beside
/// the <c>/articles</c> listing in <see cref="PublicArticleEndpoints"/>.
/// </summary>
/// <remarks>
/// <para>
/// Three of the four are public pages in the sense <see cref="PublicDocuments"/>
/// gives the word: no form, no cookie, no per-visit value, so each is stored in
/// the output cache and labelled cacheable for whatever sits in front. The
/// article page is the exception and is declared as nothing: every visit adds one
/// to the article's view count, so a copy served from a cache would be a visit
/// nobody counted (R6, R7).
/// </para>
/// <para>
/// A slug is checked against <see cref="Slugs.Pattern"/> in the handler rather
/// than by a <c>regex</c> route constraint. The slim builder registers routing
/// without the regex constraint resolver, so the inline form fails at startup
/// under it, and a handler check answers the same 404 an unknown or unpublished
/// slug gets, which is the equality R3 asks for.
/// </para>
/// <para>
/// Each route's lambda is one call to a method declared to return
/// <c>Task&lt;IResult&gt;</c>, not the handler body itself. The Request Delegate
/// Generator cannot see what another generator emitted, so a lambda whose return
/// expression is <c>Templates.*.Result(...)</c> has no return type from where it
/// stands and it emits code that does not compile. A declared return type is
/// what it needs; <see cref="AdminSignIn"/> takes the same shape. The two routes
/// with no route value take their seam as a parameter for a second reason: a
/// lambda of <c>HttpContext</c> alone is a <c>RequestDelegate</c> to overload
/// resolution, and that overload discards the result (ASP0016).
/// </para>
/// </remarks>
internal static class PublicSite
{
    public static IEndpointRouteBuilder MapPublicSite(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", (IPublishedArticles articles, HttpContext http) => HomeAsync(articles, http))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
            .AsPublicPage(PublicPageCache.Csp(Templates.Site.Home.InlineScriptCspHashes));

        endpoints.MapGet("/articles/{slug}", (string slug, HttpContext http) => ArticleAsync(slug, http))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy);

        endpoints.MapGet("/tags", (IPublicTags tags, HttpContext http) => TagsAsync(tags, http))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
            .AsPublicPage(PublicPageCache.Csp(Templates.Tags.Index.InlineScriptCspHashes));

        endpoints.MapGet("/tags/{slug}", (string slug, HttpContext http) => TagAsync(slug, http))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
            .AsPublicPage(PublicPageCache.Csp(Templates.Tags.Show.InlineScriptCspHashes));

        return endpoints;
    }

    private static async Task<IResult> HomeAsync(IPublishedArticles articles, HttpContext http)
    {
        IReadOnlyList<PublishedArticle> recent = await articles
            .RecentAsync(PublicArticleEndpoints.PageSize, http.RequestAborted).ConfigureAwait(false);
        LocaleCatalog locale = Locales.Get(http.GetLocale());

        return Templates.Site.Home.Result(
            locale: locale,
            Title: locale["hero.title"],
            AntiforgeryToken: "",
            FlashOk: "",
            FlashError: "",
            Articles: recent);
    }

    // Counted before it is read, and 404 when either step says there is no such
    // published article. The count is the write; a request the store refuses to
    // count is one it would not have shown either.
    private static async Task<IResult> ArticleAsync(string slug, HttpContext http)
    {
        if (!Slugs.IsValid(slug))
        {
            return Results.NotFound();
        }

        IPublishedArticles articles = http.RequestServices.GetRequiredService<IPublishedArticles>();
        if (!await articles.CountViewAsync(slug, http.RequestAborted).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        ArticleDetail? detail = await articles.BySlugAsync(slug, http.RequestAborted).ConfigureAwait(false);
        if (detail is null)
        {
            return Results.NotFound();
        }

        LocaleCatalog locale = Locales.Get(http.GetLocale());
        return Templates.Articles.Show.Result(
            locale: locale,
            Title: detail.Value.Title,
            AntiforgeryToken: "",
            FlashOk: "",
            FlashError: "",
            Article: detail.Value,
            Paragraphs: Paragraphs(detail.Value.Body));
    }

    private static async Task<IResult> TagsAsync(IPublicTags tags, HttpContext http)
    {
        IReadOnlyList<TagSummary> all = await tags.ListAsync(http.RequestAborted).ConfigureAwait(false);
        LocaleCatalog locale = Locales.Get(http.GetLocale());

        return Templates.Tags.Index.Result(
            locale: locale,
            Title: locale["tags.title"],
            AntiforgeryToken: "",
            FlashOk: "",
            FlashError: "",
            Tags: all);
    }

    private static async Task<IResult> TagAsync(string slug, HttpContext http)
    {
        if (!Slugs.IsValid(slug))
        {
            return Results.NotFound();
        }

        IPublicTags tags = http.RequestServices.GetRequiredService<IPublicTags>();
        TagRef? tag = await tags.GetAsync(slug, http.RequestAborted).ConfigureAwait(false);
        if (tag is null)
        {
            return Results.NotFound();
        }

        IPublishedArticles articles = http.RequestServices.GetRequiredService<IPublishedArticles>();
        IReadOnlyList<PublishedArticle> tagged = await articles
            .ByTagAsync(slug, PublicArticleEndpoints.PageSize, http.RequestAborted).ConfigureAwait(false);
        LocaleCatalog locale = Locales.Get(http.GetLocale());

        return Templates.Tags.Show.Result(
            locale: locale,
            Title: tag.Value.Name,
            AntiforgeryToken: "",
            FlashOk: "",
            FlashError: "",
            Name: tag.Value.Name,
            Articles: tagged);
    }

    // The body is plain text an admin typed into a textarea, and a blank line is
    // its only structure. Split here rather than in the template so the page
    // renders one <p> per paragraph and the template stays a template: it writes
    // values and never parses them. Each paragraph is still encoded on output.
    internal static IReadOnlyList<string> Paragraphs(string body) =>
        body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
