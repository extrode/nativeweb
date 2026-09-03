using System.Security.Cryptography;

using NativeWeb.Permissions;
using NativeWeb.SignIn;

[assembly: PermissionScope("probe")]

namespace NativeWeb.SignIn.Tests.Aot.ProbeHost;

/// <summary>
/// A model carrying <c>[Permissions]</c>, so the generator emits its constants and
/// the catalog and the analysers have generated code to walk (spec 024 R8).
/// </summary>
/// <remarks>
/// No store of its own and no scaffolding: it exists to be compiled, like
/// everything else in this project.
/// </remarks>
[Permissions("publish")]
public sealed class Widget
{
    /// <summary>The key, so the class looks like what a consumer would decorate.</summary>
    public int Id { get; set; }
}

/// <summary>
/// Every public entry point the library has, reachable from <c>Main</c> so the trim and AOT
/// analysers walk all of it.
/// </summary>
/// <remarks>
/// Never run. <c>dotnet publish</c> compiles and links it, and the gate beside this project
/// reads the warnings out of that.
/// </remarks>
public sealed class Program
{
    /// <summary>The entry point, which exists to be analysed rather than executed.</summary>
    /// <param name="args">Command-line arguments, unused.</param>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.AddSignIn(options =>
        {
            options.AuthorizationEndpoint = "https://provider.invalid/authorize";
            options.TokenEndpoint = "https://provider.invalid/token";
            options.UserInfoEndpoint = "https://provider.invalid/userinfo";
            options.IntrospectionEndpoint = "https://provider.invalid/introspect";
            options.ClientId = "probe";
            options.ClientSecret = "probe";
            options.RedirectUri = "https://host.invalid/signin/callback";
            options.StoreEncryptionKey = RandomNumberGenerator.GetBytes(32);
        })
        .OnSignedIn((http, subject, contact, ct) => ValueTask.FromResult<string?>(subject.Value))
        .WithSignInPolicy((http, ct) => ValueTask.FromResult(true));

        builder.Services.AddPermissions(PermissionCatalog.All);
        builder.Services.AddInMemoryPermissionStore();

        WebApplication app = builder.Build();

        app.MapSignIn();
        app.MapAccessAdmin(
            "/admin",
            (http, title, body) => Results.Content($"<h1>{title}</h1>{body}", "text/html"),
            _ => string.Empty);
        app.MapGet("/widgets", () => Results.Text("widgets"))
            .AddEndpointFilter(new PermissionEndpointFilter(WidgetPermissions.View));
        app.MapGet("/widgets/{id}", () => Results.Text("widget"))
            .AddEndpointFilter(new PermissionEndpointFilter(WidgetPermissions.Change, "id"));
        app.MapGet("/api", () => Results.Text("api")).RequireBearerToken();
        app.MapGet("/private", () => Results.Text("private")).RequireAuthorization();

        app.Run();
    }
}
