using System.Security.Cryptography;

using NativeWeb.SignIn;

namespace NativeWeb.SignIn.Tests.Aot.ProbeHost;

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

        WebApplication app = builder.Build();

        app.MapSignIn();
        app.MapGet("/api", () => Results.Text("api")).RequireBearerToken();
        app.MapGet("/private", () => Results.Text("private")).RequireAuthorization();

        app.Run();
    }
}
