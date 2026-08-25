using NativeWeb.Sample.Render;

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

    public static IEndpointRouteBuilder MapPublicArticles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/articles", async (
            IPublishedArticles articles,
            CancellationToken ct) =>
        {
            IReadOnlyList<PublishedArticle> recent = await articles.RecentAsync(PageSize, ct).ConfigureAwait(false);
            return Results.Content(ArticlesPage.Render(recent), "text/html");
        });

        return endpoints;
    }
}
