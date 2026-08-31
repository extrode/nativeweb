using Microsoft.AspNetCore.Antiforgery;

using NativeWeb.Render;
using NativeWeb.SignIn;
using NativeWeb.Web;

namespace __PRODUCT__;

// Explicit entry-point class (no top-level statements, per spec 003 R8). Non-static
// because WebApplicationFactory<Program> in the integration tests needs Program as a
// type argument, which a static class cannot be.
//
// This file was 487 lines until 2026-08-31, and every one of them was framework
// wiring copied into each product at stamp time -- Kestrel limits, forwarded
// headers, telemetry, data protection, antiforgery, rate limiting, the output
// cache, the middleware order, /healthz, /version, /readyz, /csp-report,
// security.txt. A security fix to any of it could not reach a product already
// stamped. It lives in libs/nativeweb.web now (spec 015), and what is left here is
// what a product actually owns: its configuration section name, its routes, and
// its templates. Nothing below is hidden -- every call is a public method on
// NativeWebPipeline, and a product needing something between two of them writes
// them out instead.
public sealed class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
        builder.AddNativeWebUserSecrets<Program>();

        // Every key this app reads sits under __PRODUCT__: in configuration. A
        // malformed value stops startup here with a named error rather than
        // becoming a silent default.
        var options = NativeWebOptions.FromConfiguration(builder.Configuration, "__PRODUCT__");
        // typeof(Program).Assembly is what /version and the telemetry service
        // version read from. Under the integration tier the entry assembly is
        // testhost, so naming this one is what keeps the stamped version honest.
        builder.AddNativeWebWeb(options, typeof(Program).Assembly);

        // Sign-in against an external provider (spec 016). Registered only when one
        // is configured, so a product stamped before it has a provider still boots:
        // the same degrade-rather-than-crash posture as the connection string above.
        // Every key is under __PRODUCT__:SignIn; see libs/nativeweb.signin/README.md.
        // Registering it is what gives RequireAuthorization a scheme to challenge
        // with; without it a protected route answers a bare 401 to everybody.
        IConfigurationSection signIn = builder.Configuration.GetSection("__PRODUCT__:SignIn");

        if (signIn["AuthorizationEndpoint"] is { Length: > 0 })
        {
            builder.Services.AddSignIn(signIn);
        }

        WebApplication app = builder.Build();

        app.MapStaticAssets();
        // Authentication in the pipeline's own slot, ahead of antiforgery: the
        // antiforgery check compares against the user as it stands when it runs, and
        // a signed-in visitor seen as anonymous fails it on the sign-out form first.
        app.UseNativeWebPipeline(Locales.All, auth =>
        {
            auth.UseAuthentication();
            auth.UseAuthorization();
        });

        // Built once, right after MapStaticAssets -- endpoints are fully known
        // here, which is what lets AssetUrls resolve a logical path to the
        // fingerprinted route the page should link.
        var assets = new AssetUrls(app);
        IAntiforgery antiforgery = app.Services.GetRequiredService<IAntiforgery>();

        // From the template's own generated hashes (spec 006), so the CSP cannot
        // drift from the scripts the page renders.
        string homeCsp = PublicPageCache.Csp(Templates.Home.Index.InlineScriptCspHashes);

        // The lambda's return type is stated because the RDG interceptor binds
        // before the template generator's output exists in its view of the
        // compilation; left inferred, the return type is unresolvable there.
        app.MapGet("/", IResult (HttpContext context) =>
        {
            // Cookie-free requests get the byte-stable anonymous variant and are
            // marked cacheable; a flash renders today's page unchanged.
            Chrome chrome = PublicPageCache.CarriesIdentityCookie(context.Request)
                ? Chrome.For(context, assets, antiforgery, Locales.Get(context.GetLocale()))
                : Chrome.Anonymous(context, assets, Locales.Get(context.GetLocale()));

            // Read-once. Never take a flash in a fragment path, which would
            // consume it invisibly.
            (string Ok, string Error) flash = Flash.Take(context);
            return Templates.Home.Index.Result(
                locale: chrome.Locale,
                Title: chrome.Locale["title.home"],
                Assets: chrome.Assets,
                Nonce: chrome.Nonce,
                AntiforgeryFieldName: chrome.AntiforgeryFieldName,
                AntiforgeryToken: chrome.AntiforgeryToken,
                BotCanary: chrome.BotCanary,
                FlashOk: flash.Ok,
                FlashError: flash.Error);
        }).RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy).AsPublicPage(homeCsp);

        // The sign-in, callback and sign-out endpoints, at the configured paths.
        if (signIn["AuthorizationEndpoint"] is { Length: > 0 })
        {
            app.MapSignIn();
        }

        // /healthz, /version, /readyz, /csp-report and /.well-known/security.txt.
        app.MapNativeWebEndpoints(options, typeof(Program).Assembly);

        app.Run();
    }
}
