using System.Net;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.TestHost;

using __PRODUCT__;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// <c>UseNativeWebPipeline</c>'s authentication callback runs in front of the
/// antiforgery middleware, and that position is the only reason the parameter
/// exists.
/// </summary>
/// <remarks>
/// <para>
/// Antiforgery tokens are bound to the identity that minted them, and the
/// validating middleware compares against <c>HttpContext.User</c> as it stands
/// when it runs. A product that called <c>UseAuthentication</c> after this
/// pipeline would have every authenticated form post rejected as a 400 — which
/// is what happened on 2026-08-31 when <c>apps/sample</c> first adopted the seam:
/// ten create, update and delete facts failed with BadRequest and no other
/// symptom, and nothing was wrong except where two lines sat.
/// </para>
/// <para>
/// Asserted through <see cref="IAntiforgeryValidationFeature"/> rather than by
/// listing middleware types. The feature is what antiforgery sets as it runs, so
/// its absence inside the callback and its presence after is a direct reading of
/// the order — and a test that enumerated registrations would pass over a
/// pipeline with the order right and the wiring wrong.
/// </para>
/// </remarks>
public sealed class AuthenticationSlotTests
{
    private static async Task<(HttpStatusCode Status, bool? InsideCallback, bool? AtTheEndpoint)> PostAsync()
    {
        bool? insideCallback = null;
        bool? atTheEndpoint = null;

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = "wwwroot",
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.AddNativeWebWeb(new NativeWebOptions
        {
            ApplicationName = "Demo",
            ConfigurationSection = "Demo",
            TelemetryServiceName = "Demo",
            DataProtectionKeyDirectory = Path.Combine(AppContext.BaseDirectory, "dp-keys"),
            UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads"),
        });

        WebApplication app = builder.Build();

        app.UseNativeWebPipeline(Locales.All, auth => auth.Use(async (context, next) =>
        {
            insideCallback = context.Features.Get<IAntiforgeryValidationFeature>() is not null;
            await next(context);
        }));

        // Binds the form, which is what puts antiforgery metadata on the
        // endpoint: UseAntiforgery validates a POST only where the handler
        // actually reads one, so a parameterless handler is never checked.
        app.MapPost("/form", (HttpContext context, IFormCollection form) =>
        {
            atTheEndpoint = context.Features.Get<IAntiforgeryValidationFeature>() is not null;
            return Results.Ok();
        });

        await app.StartAsync();

        try
        {
            HttpClient client = app.GetTestClient();
            client.BaseAddress = new Uri("https://localhost/");

            using var body = new FormUrlEncodedContent([new KeyValuePair<string, string>("x", "1")]);
            using HttpResponseMessage response = await client.PostAsync(new Uri("/form", UriKind.Relative), body);

            return (response.StatusCode, insideCallback, atTheEndpoint);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheCallbackRunsBeforeAntiforgeryHasValidatedAnything()
    {
        (HttpStatusCode status, bool? insideCallback, bool? atTheEndpoint) = await PostAsync();

        // Ran at all, and ran before the feature existed.
        Assert.False(insideCallback);

        // The control on the other side: this post carries no token, so
        // antiforgery did run, did record a failure, and the guard turned it into
        // a 400 rather than letting the endpoint bind the form. Without this the
        // assertion above would pass over a pipeline that had no antiforgery in
        // it at all.
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(atTheEndpoint);
    }
}
