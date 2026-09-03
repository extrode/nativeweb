using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

using NativeWeb.Permissions;
using NativeWeb.Scaffold;
using NativeWeb.Scaffold.Generated;

[assembly: PermissionScope("publicread")]

namespace NativeWeb.Permissions.Tests.Integration.PublicReadHost;

/// <summary>A note, listed by anyone and written by almost nobody.</summary>
[Scaffold(Display = "Notes", Route = "notes")]
[Auth(Role = "admin", PublicRead = true)]
[Permissions]
public sealed class Note
{
    /// <summary>The identity in every URL.</summary>
    [ScaffoldKey]
    public string Id { get; set; } = "";

    /// <summary>The one column.</summary>
    [ScaffoldList]
    public string Title { get; set; } = "";
}

/// <summary>The whole store: one seeded row, held in a list.</summary>
internal sealed class NoteStore : IScaffoldStore<Note>
{
    private readonly List<Note> _notes = [new Note { Id = "1", Title = "Readable by anyone" }];

    public ValueTask<IReadOnlyList<Note>> ListAsync(
        string? search,
        IReadOnlyList<string> searchFields,
        ScaffoldSortSpec? sort,
        IReadOnlyList<string> sortFields,
        ScaffoldCursor? after,
        int take,
        CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<Note>>(_notes);

    public ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct) =>
        ValueTask.FromResult(_notes.Count);

    public ValueTask<Note?> GetAsync(string id, CancellationToken ct) =>
        ValueTask.FromResult(_notes.Find(n => n.Id == id));

    public ValueTask<string> CreateAsync(Note item, CancellationToken ct)
    {
        _notes.Add(item);
        return ValueTask.FromResult(item.Id);
    }

    public ValueTask<bool> UpdateAsync(string id, Note item, CancellationToken ct) => ValueTask.FromResult(true);

    public ValueTask<bool> DeleteAsync(string id, CancellationToken ct) => ValueTask.FromResult(true);
}

/// <summary>
/// The host. Signs in whoever a header names, so a test can be anonymous, signed
/// in without the role, or signed in with it.
/// </summary>
public sealed class Program
{
    /// <summary>Names the subject to sign in as; absent means anonymous.</summary>
    public const string SubjectHeader = "X-Test-Subject";

    /// <summary>The entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.AddScaffolding();
        builder.Services.AddSingleton<IScaffoldStore<Note>>(new NoteStore());
        builder.Services.AddPermissions(PermissionCatalog.All);
        builder.Services.AddInMemoryPermissionStore();

        builder.Services.AddAuthentication(HeaderScheme.Name)
            .AddScheme<AuthenticationSchemeOptions, HeaderScheme>(HeaderScheme.Name, null);
        builder.Services.AddAuthorization();

        WebApplication app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapScaffolding();

        app.Run();
    }

    private sealed class HeaderScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string Name = CookieAuthenticationDefaults.AuthenticationScheme;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubjectHeader, out Microsoft.Extensions.Primitives.StringValues subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, subject.ToString()), new Claim(ClaimTypes.Role, "admin")],
                Name);

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
