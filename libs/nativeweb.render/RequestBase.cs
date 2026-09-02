using Microsoft.AspNetCore.Http;

namespace NativeWeb.Render;

/// <summary>
/// The absolute base a feed or sitemap resolves its links against (spec 021 R4).
/// </summary>
public static class RequestBase
{
    /// <summary>
    /// <c>scheme://host/</c> as the request presents them. Behind the framework
    /// pipeline both have already been rewritten from the forwarded headers of a
    /// configured proxy, so this is the public origin, not the loopback Kestrel
    /// listens on. A caller that knows better passes its own <see cref="Uri"/> to
    /// the helper instead.
    /// </summary>
    /// <param name="request">The current request.</param>
    /// <returns>The origin with a trailing slash.</returns>
    public static Uri PublicBase(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new Uri($"{request.Scheme}://{request.Host.Value}/", UriKind.Absolute);
    }
}
