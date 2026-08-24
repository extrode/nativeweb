using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using Npgsql;

using NativeWeb.Scaffold;
using NativeWeb.Scaffold.Generated;

namespace NativeWeb.Sample;

/// <summary>
/// The scaffolding demo. Note what is absent: no controller, no view, no form
/// binding, no route table. Everything under <c>/admin</c> comes from the two
/// decorated classes in <c>Models.cs</c>.
/// </summary>
// Public, not internal: WebApplicationFactory<Program> in the integration tier
// needs the entry-point type, same as apps/web/Program.cs.
public sealed class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

        // Antiforgery and authorization, which every scaffolded route needs.
        builder.Services.AddScaffolding();

        // Authentication is deliberately NOT part of that call: who may reach /admin
        // is this app's decision, and a default from a library is one nobody reviews.
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "nw-sample";
                options.LoginPath = "/signin";
            });

        // The generated endpoints resolve IScaffoldStore<T> from RequestServices and
        // know nothing else about persistence. Which implementation answers is decided
        // here, once, and no generated code is regenerated when it changes.
        //
        // Same degrade-rather-than-crash posture as apps/web: a missing connection
        // string is not an error, it selects the in-memory store. That keeps
        // `dotnet run`, the integration tier and the Playwright tier working with no
        // database, while a configured string exercises the JauntyQ path against a
        // real one. Both are IScaffoldStore<Article>; nothing above this line differs.
        var tags = new TagStore();
        builder.Services.AddSingleton<IScaffoldStore<Tag>>(tags);

        string? connString = builder.Configuration.GetConnectionString("Sample");
        if (string.IsNullOrWhiteSpace(connString))
        {
            var articles = new ArticleStore();
            builder.Services.AddSingleton<IScaffoldStore<Article>>(articles);
            Seed.Fill(articles, tags);
        }
        else
        {
            NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(connString).Build();
            builder.Services.AddSingleton(dataSource);

            // Singleton, not scoped: the store holds no per-request state and takes a
            // connection from the pool per call. See JauntyArticleStore's remarks.
            builder.Services.AddSingleton<IScaffoldStore<Article>>(new JauntyArticleStore(dataSource));

            // Tags are still in-memory, deliberately. Two models behind one seam with
            // two entirely different implementations is the claim this demo is making,
            // and it is only visible if they actually differ.
            Seed.FillTags(tags);
        }

        WebApplication app = builder.Build();

        // Authentication, authorization, antiforgery, and the guard that turns a failed
        // antiforgery check into a 400 instead of a 500. Nothing is hidden: this is four
        // public calls, and an app needing anything between them writes them out. See
        // ScaffoldApplicationBuilderExtensions.UseScaffolding.
        app.UseScaffolding();

        // Every route under /admin, for every [Scaffold] model in this assembly. This
        // one call is the whole of the admin UI's wiring.
        app.MapScaffolding();

        app.MapGet("/", () => Results.Redirect("/admin"));

        // Demo sign-in. Gated to Development because it grants the admin role for the
        // asking, which is only acceptable in something nobody deploys.
        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/signin", async (HttpContext http) =>
            {
                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "demo"), new Claim(ClaimTypes.Role, "admin")],
                    CookieAuthenticationDefaults.AuthenticationScheme);

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity)).ConfigureAwait(false);

                return Results.Redirect("/admin");
            });
        }

        app.Run();
    }
}

internal static class Seed
{
    internal static void Fill(ArticleStore articles, TagStore tags)
    {
        FillTags(tags);
        articles.Seed(new Article
        {
            Id = "a1",
            Title = "Lean Native Web",
            Author = "Sy",
            Body = "Server-rendered HTML, one native binary, no client build step.",
            Published = true,
            ViewCount = 1420,
            CreatedAt = new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a2",
            Title = "Binding forms under NativeAOT",
            Author = "Sy",
            Body = "Complex-type [FromForm] does not compile. IBindableFromHttpContext does.",
            Published = true,
            ViewCount = 388,
            CreatedAt = new DateTime(2026, 6, 18, 14, 5, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a3",
            Title = "Scaffolding without reflection",
            Author = null,
            Body = "Draft.",
            Published = false,
            ViewCount = 0,
            CreatedAt = new DateTime(2026, 8, 19, 8, 0, 0, DateTimeKind.Utc),
        });

    }

    // Split out because the JauntyQ path seeds tags but not articles: article rows
    // live in PostgreSQL and are whatever the database already holds, which is the
    // whole difference between a demo store and a real one.
    internal static void FillTags(TagStore tags)
    {
        tags.Seed(new Tag { Slug = "aot", Name = "NativeAOT", Color = "#5bbd83" });
        tags.Seed(new Tag { Slug = "htmx", Name = "HTMX", Color = "#7aa2f7" });
    }
}
