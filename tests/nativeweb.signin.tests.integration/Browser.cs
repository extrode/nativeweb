using System.Net;

namespace NativeWeb.SignIn.Tests.Integration;

/// <summary>
/// The browser half of the round trip: keeps cookies, follows redirects, records what it saw.
/// </summary>
/// <remarks>
/// Written out rather than taken from <c>WebApplicationFactory</c>'s cookie-handling client
/// because the cookie's ATTRIBUTES are part of what these tests assert, and a container that has
/// already parsed the header away leaves nothing to assert against.
/// </remarks>
public sealed class Browser(HttpClient client) : IDisposable
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    /// <summary>Every <c>Set-Cookie</c> header seen, in order.</summary>
    public List<string> SetCookies { get; } = [];

    /// <summary>Every location redirected to, in order.</summary>
    public List<string> Redirects { get; } = [];

    /// <summary>Whether a cookie of this name is currently held.</summary>
    /// <param name="name">The cookie name.</param>
    /// <returns>True when the jar holds a non-empty value.</returns>
    public bool Holds(string name) => _cookies.TryGetValue(name, out string? v) && v.Length > 0;

    /// <summary>Issues a GET and follows redirects to the end.</summary>
    /// <param name="path">Where to start.</param>
    /// <param name="hops">How many redirects to allow before giving up.</param>
    /// <returns>The response that was not a redirect.</returns>
    public async Task<HttpResponseMessage> GetAsync(string path, int hops = 10)
    {
        string next = path;

        for (int hop = 0; hop <= hops; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            HttpResponseMessage response = await SendAsync(request).ConfigureAwait(false);

            if (!IsRedirect(response))
            {
                return response;
            }

            next = response.Headers.Location!.ToString();
            Redirects.Add(next);
            response.Dispose();
        }

        throw new InvalidOperationException($"More than {hops} redirects starting at {path}.");
    }

    /// <summary>Issues a GET and returns the first response, redirect or not.</summary>
    /// <param name="path">Where to go.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> GetOnceAsync(string path)
    {
        // Awaited before the request is disposed. The earlier shape was `using var request` in a
        // non-async method returning the task: the using disposed the message the moment the method
        // returned, which is while HttpClient still holds it.
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request).ConfigureAwait(false);
    }

    /// <summary>Issues a form POST and returns the first response.</summary>
    /// <param name="path">Where to post.</param>
    /// <param name="fields">The form body.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> PostAsync(
        string path, IEnumerable<KeyValuePair<string, string>> fields)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields),
        };

        return await SendAsync(request).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        if (_cookies.Count > 0)
        {
            request.Headers.Add(
                "Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        }

        HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? headers))
        {
            foreach (string header in headers)
            {
                SetCookies.Add(header);
                Remember(header);
            }
        }

        return response;
    }

    private void Remember(string header)
    {
        string pair = header.Split(';')[0];
        int equals = pair.IndexOf('=', StringComparison.Ordinal);

        if (equals <= 0)
        {
            return;
        }

        string name = pair[..equals];
        string value = pair[(equals + 1)..];

        if (value.Length == 0 || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
        {
            _cookies.Remove(name);
            return;
        }

        _cookies[name] = value;
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Redirect
            or HttpStatusCode.MovedPermanently or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <inheritdoc/>
    public void Dispose() => client.Dispose();
}
