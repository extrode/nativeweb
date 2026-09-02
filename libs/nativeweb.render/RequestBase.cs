using Microsoft.AspNetCore.Http;

namespace NativeWeb.Render;

/// <summary>
/// The absolute base a feed or sitemap resolves its links against (spec 021 R4).
/// </summary>
public static class RequestBase
{
    /// <summary>
    /// <c>scheme://host/</c> as the request presents them. Behind the framework
    /// pipeline the scheme has been taken from <c>X-Forwarded-Proto</c> of a
    /// configured proxy, and the host is the <c>Host</c> header that proxy passed
    /// through, which for a reverse proxy serving one site is the public name.
    /// Nothing here validates the host: a request that reaches Kestrel directly
    /// with a made-up <c>Host</c> gets that name back in its own links, and the
    /// output cache keys on the host, so it shares that answer with nobody.
    /// A caller that knows better passes its own <see cref="Uri"/> to the helper
    /// instead.
    /// </summary>
    /// <param name="request">The current request.</param>
    /// <returns>The origin with a trailing slash.</returns>
    public static Uri PublicBase(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new Uri($"{request.Scheme}://{request.Host.Value}/", UriKind.Absolute);
    }
}
