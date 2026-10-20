namespace __PRODUCT__.Tests.E2e;

// The e2e app serves https with the fixture's own self-signed certificate
// (__PRODUCT__ProcessFixture) -- a __Host- session cookie is Secure-always and
// Chromium refuses the prefix on an http origin, so the whole tier is https.
// Raw HttpClients bypass chain validation here the same way the browser
// contexts set IgnoreHTTPSErrors.
internal static class E2eHttp
{
    public static HttpClient CreateClient() => new(Handler());

    public static HttpClientHandler Handler() => new()
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    };
}
