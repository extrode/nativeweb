using System.Collections.Frozen;

using Microsoft.AspNetCore.StaticAssets;
using Microsoft.AspNetCore.StaticAssets.Infrastructure;

namespace __PRODUCT__.Render;

// Resolves a logical wwwroot path (e.g. "/css/tokens.css") to the fingerprinted
// URL MapStaticAssets actually serves it at, so the Render layer never hardcodes
// a <link>/<script> src that content-hashing would otherwise break. Built once
// from the endpoint route builder right after app.MapStaticAssets() -- endpoints
// are fully known at that point, so there's nothing to invalidate later.
// Skeleton copy -- the product owns this file after stamping.
public sealed class AssetUrls
{
    // Frozen (R37): built once at startup, then read on every render -- the
    // freeze cost is paid where startup already pays a publish-sized bill.
    private readonly FrozenDictionary<string, string> _routesByAssetPath;

    public AssetUrls(IEndpointRouteBuilder endpoints)
    {
        IReadOnlyList<StaticAssetDescriptor> descriptors = StaticAssetsEndpointDataSourceHelper.ResolveStaticAssetDescriptors(endpoints, manifestPath: null);
        var routesByAssetPath = new Dictionary<string, string>(StringComparer.Ordinal);

        // Every logical asset shows up as several descriptors: a plain identity
        // route, a fingerprinted (content-hashed) route, and a distinct entry for
        // its precompressed .gz sibling. Always prefer the fingerprinted one --
        // it's the one MapStaticAssets marks with a "fingerprint" property -- so
        // callers get long-lived cacheable URLs regardless of descriptor order.
        foreach (StaticAssetDescriptor descriptor in descriptors)
        {
            if (descriptor.AssetPath.EndsWith(".gz", StringComparison.Ordinal))
            {
                continue;
            }

            string key = "/" + descriptor.AssetPath.TrimStart('/');
            bool isFingerprinted = descriptor.Properties.Any(p => p.Name == "fingerprint");
            if (isFingerprinted || !routesByAssetPath.ContainsKey(key))
            {
                routesByAssetPath[key] = "/" + descriptor.Route.TrimStart('/');
            }
        }

        _routesByAssetPath = routesByAssetPath.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public string Resolve(string assetPath) => _routesByAssetPath.TryGetValue(assetPath, out string? route)
        ? route
        : throw new InvalidOperationException($"Unknown static asset '{assetPath}'. Check the path matches a file under wwwroot.");
}
