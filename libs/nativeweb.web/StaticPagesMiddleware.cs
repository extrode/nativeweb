using System.Collections.Frozen;

using Microsoft.Extensions.FileProviders;

namespace NativeWeb.Web;

// Hand-authored wwwroot/pages/*.html served at clean URLs (spec 003 R38).
// This middleware only rewrites the request path before routing runs; the
// serving itself is MapStaticAssets, which is where ETag revalidation,
// Cache-Control: no-cache for non-fingerprinted assets, and (at publish)
// precompressed bodies already live -- none of that is reimplemented here.
// The marker in HttpContext.Items tells SecurityHeadersMiddleware to emit
// the stricter nonce-free CSP and to leave the static pipeline's caching
// headers alone. The raw /pages/x.html path stays reachable through the
// static pipeline too; it gets the dynamic header set there, which is
// strictly tighter caching, so nothing leaks through the ugly URL. One
// side effect of rewriting this early: request logs record the rewritten
// /pages/x.html path, not the clean URL the client sent. Skeleton copy --
// the product owns this file after stamping.
public sealed class StaticPagesMiddleware
{
    // HttpContext.Items marker: present means this response is a static page
    // (or a static-styled error page) and gets the nonce-free CSP branch.
    public const string ItemKey = "static-page";

    private readonly RequestDelegate _next;
    private readonly FrozenDictionary<string, string> _routes;

    // Instantiated once when the pipeline is built, after every Map* call has
    // registered its endpoints -- which is what lets the collision guard see
    // the full route table.
    public StaticPagesMiddleware(
        RequestDelegate next,
        IWebHostEnvironment environment,
        IEnumerable<EndpointDataSource> endpointSources)
        : this(next, BuildRouteMap(environment.WebRootFileProvider))
    {
        GuardAgainstRouteCollisions(_routes.Keys, endpointSources);
    }

    public StaticPagesMiddleware(RequestDelegate next, FrozenDictionary<string, string> routes)
    {
        _next = next;
        _routes = routes;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Value is { } path && _routes.TryGetValue(path, out string? file))
        {
            context.Items[ItemKey] = true;
            context.Request.Path = file;
        }

        return _next(context);
    }

    // "/about" <- pages/about.html: the clean URL is the filename minus its
    // extension. error.html is the ErrorPageMiddleware shell, not a page
    // anyone navigates to, so it gets no clean URL. Subdirectories are
    // deliberately not walked -- a nested page gets no clean URL, only its
    // raw /pages/... path. Filenames are expected to be plain ASCII; a name
    // needing percent-encoding would not round-trip through the decoded
    // Request.Path this map is keyed on. A missing pages/ dir is valid (an
    // app with no static pages yet) and yields an empty map.
    public static FrozenDictionary<string, string> BuildRouteMap(IFileProvider webRoot)
    {
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IFileInfo file in webRoot.GetDirectoryContents("pages"))
        {
            if (file.IsDirectory || !file.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = file.Name[..^".html".Length];
            if (name.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            routes["/" + name] = "/pages/" + file.Name;
        }

        return routes.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    // The rewrite is unconditional and runs before routing, so a pages/ file
    // whose clean URL matches a mapped route would silently shadow that route
    // for every HTTP method -- dropping settings.html into pages/ must fail
    // the deploy at startup, not disable the settings screen.
    public static void GuardAgainstRouteCollisions(
        IEnumerable<string> cleanUrls, IEnumerable<EndpointDataSource> endpointSources)
    {
        var routeTexts = new HashSet<string>(
            endpointSources
                .SelectMany(static source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(static endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (string url in cleanUrls)
        {
            if (routeTexts.Contains(url))
            {
                throw new InvalidOperationException(
                    $"wwwroot/pages{url}.html would shadow the mapped route '{url}'; rename the page or remove the route.");
            }
        }
    }
}
