using System.Globalization;

using Microsoft.AspNetCore.Antiforgery;

using NativeWeb.Forms;
using NativeWeb.Render;

namespace NativeWeb.Sample;

/// <summary>
/// <c>GET /articles</c> — the public, read-only feed of published articles.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to everything under <c>/admin</c>: those routes are generated
/// from the attributes on <see cref="Article"/> and never appear in source, while
/// this one is written by hand. Both shapes are normal, and a real product is
/// mostly the hand-written kind — scaffolding earns its place on CRUD over a
/// table, not on the pages people actually visit.
/// </para>
/// <para>
/// No authentication and no antiforgery: it is a GET that changes nothing and
/// shows only rows already marked published. Adding <c>RequireAuthorization</c>
/// here would be the thing to reconsider, not omitting it.
/// </para>
/// </remarks>
internal static class PublicArticleEndpoints
{
    // How many articles the page shows. A constant rather than a query-string
    // parameter on purpose: an attacker-controlled page size is a denial-of-service
    // lever, and a demo feed has no reason to offer one.
    private const int PageSize = 20;

    // The most the filter form will show, whatever it asks for. Same reasoning as
    // PageSize: the number comes from the request now, so it needs a ceiling.
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapPublicArticles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/articles", async (
            IAntiforgery antiforgery,
            HttpContext http,
            IPublishedArticles articles,
            CancellationToken ct) =>
        {
            IReadOnlyList<PublishedArticle> recent = await articles.RecentAsync(PageSize, ct).ConfigureAwait(false);
            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);
            return Page(http, recent, query: null, tokens);
        });

        // The declarative-binding demo (spec 014). Three things are worth reading
        // here, because all three are the point:
        //
        //  - The handler takes ArticleFilter, already bound. No ReadFormAsync, no
        //    HasFormContentType check, no per-field indexer. The binder is emitted
        //    onto the record from its [Form] attribute.
        //  - MapFormPost, not MapPost, and that is not a style choice. Measured
        //    2026-08-30: the Request Delegate Generator cannot see the generated
        //    binder, so a plain MapPost binds the model as a JSON body and answers a
        //    form POST with 415 from routing. Writing it that way is now NWF010, a
        //    build error.
        //  - Nothing here attaches the antiforgery metadata, because MapFormPost
        //    does. §12 measured a route in this shape answering 200 OK to a POST
        //    with no token, silently; the guard is now structural.
        endpoints.MapFormPost<ArticleFilter>("/articles", async (filter, http) =>
        {
            IPublishedArticles articles = http.RequestServices.GetRequiredService<IPublishedArticles>();
            IAntiforgery antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();

            int take = ParseTake(filter.Take);
            IReadOnlyList<PublishedArticle> recent = await articles
                .RecentAsync(take, http.RequestAborted).ConfigureAwait(false);
            IReadOnlyList<PublishedArticle> matching = Matching(recent, filter.Query);

            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

            // An htmx request gets the list fragment alone; a no-JS form POST gets
            // the whole page. Same handler, same data, one branch -- which is the
            // progressive-enhancement contract the architecture doc describes, and
            // the reason the form carries both hx-post and a real method/action.
            return http.Request.Headers.ContainsKey("HX-Request")
                ? Results.Content(
                    Templates.Articles.Results.ToHtmlString(
                        locale: Locales.Get(http.GetLocale()),
                        Articles: matching),
                    "text/html")
                : Page(http, matching, filter.Query, tokens);
        });

        return endpoints;
    }

    // The full page, through the compiled layout. Both handlers land here so the
    // GET and the no-JS POST cannot drift apart.
    private static IResult Page(
        HttpContext http,
        IReadOnlyList<PublishedArticle> articles,
        string? query,
        AntiforgeryTokenSet tokens) =>
        Templates.Articles.Index.Result(
            locale: Locales.Get(http.GetLocale()),
            Title: Locales.Get(http.GetLocale())["title.articles"],
            AntiforgeryToken: tokens.RequestToken ?? "",
            Query: query ?? "",
            AntiforgeryFieldName: tokens.FormFieldName,
            Articles: articles);

    // A blank box means "everything", which is why an empty query is not a filter
    // that matches nothing.
    internal static IReadOnlyList<PublishedArticle> Matching(
        IReadOnlyList<PublishedArticle> articles,
        string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return articles;
        }

        return articles
            .Where(a => a.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // The conversion the form model deliberately does not do. Anything unparseable
    // is the default rather than an error: a page size is not worth a 400, and the
    // ceiling is what stops it being a denial-of-service lever.
    internal static int ParseTake(string? raw)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int take) || take <= 0)
        {
            return PageSize;
        }

        return Math.Min(take, MaxPageSize);
    }
}
