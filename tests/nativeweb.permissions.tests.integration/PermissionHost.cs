using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NativeWeb.Sample;

using Npgsql;

namespace NativeWeb.Permissions.Tests.Integration;

/// <summary>
/// A minimal application over the real <c>nw_</c> tables: a permission-gated
/// endpoint, a role-and-permission-gated one, and a sign-in that lets each test
/// choose its own subject.
/// </summary>
/// <remarks>
/// <para>Not <c>apps/sample</c>, and the reason is the subject. The sample has two
/// sign-ins and both are seeded with the whole catalog by migration 0006, so a test
/// wanting a subject that holds nothing would have to revoke a grant out from under
/// every other test in the process. Here each test mints its own subject string and
/// nothing it does is visible to another.</para>
/// <para>The store under all of it is <see cref="JauntyPermissionStore"/>, which is
/// the repository's only <see cref="IPermissionStore"/> over PostgreSQL. Writing a
/// second one for the tests would be testing the test.</para>
/// <para><b>Nothing here truncates</b>, matching the scaffold integration tier: rows
/// accumulate in <c>nativeweb_sample_test</c> across runs, and a test that asserted
/// on a table-wide count would be the thing that breaks when two runs overlap.</para>
/// </remarks>
internal sealed class PermissionHost : IAsyncDisposable
{
    internal const string SubjectHeader = "X-Test-Subject";
    internal const string RoleHeader = "X-Test-Role";

    private static readonly Regex TokenPattern = new(
        """<input type="hidden" name="(__[^"]+)" value="([^"]*)">""",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private WebApplication _app = null!;

    internal static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ConnectionStrings__Sample")
        ?? "Host=localhost;Database=nativeweb_sample_test;Username=nativeweb_app";

    internal IPermissionStore Store { get; private set; } = null!;

    internal HttpClient Client { get; private set; } = null!;

    internal static Permission View { get; } = new("sample:article.view");

    internal static Permission Change { get; } = new("sample:article.change");

    internal static Permission Delete { get; } = new("sample:article.delete");

    internal static async Task<PermissionHost> StartAsync()
    {
        var host = new PermissionHost();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddSingleton<IPermissionStore>(new JauntyPermissionStore(dataSource));
        builder.Services.AddPermissions([View, Change, Delete]);

        builder.Services.AddAuthentication(HeaderScheme.Name)
            .AddScheme<AuthenticationSchemeOptions, HeaderScheme>(HeaderScheme.Name, null);
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();

        host._app = builder.Build();
        host._app.UseAuthentication();
        host._app.UseAuthorization();

        // After authentication and authorization, which is where the middleware
        // requires it. Mapping the access admin without it is a startup-time
        // failure now that its posts carry IAntiforgeryMetadata.
        host._app.UseAntiforgery();

        host._app.MapGet("/open", () => Results.Ok("open"));

        host._app.MapGet("/change", () => Results.Ok("changed"))
            .AddEndpointFilter(new PermissionEndpointFilter(Change));

        host._app.MapGet("/rows/{id}", () => Results.Ok("row"))
            .AddEndpointFilter(new PermissionEndpointFilter(Change, "id"));

        // R10. The role is decided by the authorization middleware, which runs
        // before any endpoint filter, so this route ANDs by ordering alone.
        host._app.MapGet("/editors-only", () => Results.Ok("edited"))
            .RequireAuthorization(policy => policy.RequireRole("editor"))
            .AddEndpointFilter(new PermissionEndpointFilter(Change));

        // R16. The access admin over the same real tables, ungated here: the
        // library returns the group so a host applies its own gate, and this host
        // is the test.
        host._app.MapAccessAdmin(
            "/admin",
            (http, title, body) => Results.Content($"<h1>{title}</h1>{body}", "text/html"),
            Token);

        await host._app.StartAsync();

        host.Store = host._app.Services.GetRequiredService<IPermissionStore>();
        host.Client = host._app.GetTestClient();
        host.Client.BaseAddress = new Uri("https://localhost/");

        return host;
    }

    /// <summary>A subject nothing else in this run or any other will use.</summary>
    internal static string NewSubject() => "test-" + Guid.NewGuid().ToString("N")[..12];

    internal async Task<HttpResponseMessage> GetAsync(string path, string? subject, string? role = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (subject is not null)
        {
            request.Headers.Add(SubjectHeader, subject);
        }

        if (role is not null)
        {
            request.Headers.Add(RoleHeader, role);
        }

        return await Client.SendAsync(request);
    }

    internal async Task<HttpResponseMessage> PostAsync(
        string path, string? subject, IEnumerable<KeyValuePair<string, string>> form)
    {
        (KeyValuePair<string, string> field, string cookie) = await TokenPairAsync(subject);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new FormUrlEncodedContent([field, .. form]),
        };

        if (subject is not null)
        {
            request.Headers.Add(SubjectHeader, subject);
        }

        request.Headers.Add("Cookie", cookie);

        return await Client.SendAsync(request);
    }

    /// <summary>The chrome's antiforgery field, rendered the way a host renders it.</summary>
    private static string Token(HttpContext http)
    {
        IAntiforgery antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

        return $"""<input type="hidden" name="{tokens.FormFieldName}" value="{tokens.RequestToken}">""";
    }

    // The pair, taken from a rendered page the way a browser takes it. The cookie
    // half is carried by hand because the TestServer client keeps no cookie jar, and
    // antiforgery refuses a request that brings only the field.
    private async Task<(KeyValuePair<string, string> Field, string Cookie)> TokenPairAsync(string? subject)
    {
        using HttpResponseMessage page = await GetAsync("/admin/access/", subject);
        page.EnsureSuccessStatusCode();

        Match match = TokenPattern.Match(await page.Content.ReadAsStringAsync());

        if (!match.Success)
        {
            throw new InvalidOperationException("The access index rendered no antiforgery field.");
        }

        string cookie = string.Join(
            "; ",
            page.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));

        return (new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value), cookie);
    }

    /// <summary>The subject's first_seen stamp, read outside the store under test.</summary>
    internal static async Task<DateTimeOffset?> FirstSeenAsync(string subject)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command =
            new NpgsqlCommand("SELECT first_seen FROM nw_subject WHERE subject = @s", connection);
        command.Parameters.AddWithValue("s", subject);

        object? value = await command.ExecuteScalarAsync();

        return value is DateTime stamp ? new DateTimeOffset(stamp, TimeSpan.Zero) : null;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// Signs in whoever the request names. R17 in one class: the permission layer
    /// reads a subject claim and does not care what issued it, so the whole of
    /// authentication here is two headers.
    /// </summary>
    private sealed class HeaderScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string Name = "header";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubjectHeader, out Microsoft.Extensions.Primitives.StringValues subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, subject.ToString())];

            if (Request.Headers.TryGetValue(RoleHeader, out Microsoft.Extensions.Primitives.StringValues role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}
