using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using Npgsql;

using NativeWeb.Scaffold;
using NativeWeb.Scaffold.Generated;
using NativeWeb.Web;

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

        // The framework's web seam (spec 015 R9). Everything a NativeWeb app needs
        // in front of its own routes -- Kestrel limits, forwarded headers, the
        // pooled data source, telemetry, data protection, antiforgery, bot traps,
        // rate limiting and the public-page output cache -- registered by one call
        // that hides none of them: every step is a public extension method on
        // NativeWebPipeline, and an app needing something between two of them
        // writes the eleven calls out instead.
        //
        // The connection string is this app's own, under ConnectionStrings:Sample
        // rather than Sample:Db:ConnectionString, because the tutorial and the
        // integration tier both set it the standard way. Everything else binds
        // from the Sample: section.
        NativeWebOptions options = NativeWebOptions.FromConfiguration(builder.Configuration, "Sample") with
        {
            ConnectionString = builder.Configuration.GetConnectionString("Sample"),
        };

        builder.AddNativeWebWeb(options);

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

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var articles = new ArticleStore();
            builder.Services.AddSingleton<IScaffoldStore<Article>>(articles);
            Seed.Fill(articles, tags);

            // The public feed reads through the same seam. In-memory it has to
            // filter and sort itself; see MemoryPublishedArticles.
            builder.Services.AddSingleton<IPublishedArticles>(new MemoryPublishedArticles(articles));
        }
        else
        {
            // The NpgsqlDataSource itself is AddNativeWebWeb's, built from the same
            // options and pooled with MaxAutoPrepare set; these two resolve it
            // rather than building a second one. Singleton, not scoped: the store
            // holds no per-request state and takes a connection from the pool per
            // call. See JauntyArticleStore's remarks.
            builder.Services.AddSingleton<IScaffoldStore<Article>>(
                sp => new JauntyArticleStore(sp.GetRequiredService<NpgsqlDataSource>()));

            // Same seam, but the filter and the ordering move into SQL. This is the
            // pair the tutorial in docs/00-quick-start/tutorial-first-feature.md compares.
            builder.Services.AddSingleton<IPublishedArticles>(
                sp => new JauntyPublishedArticles(sp.GetRequiredService<NpgsqlDataSource>()));

            // Tags are still in-memory, deliberately. Two models behind one seam with
            // two entirely different implementations is the claim this demo is making,
            // and it is only visible if they actually differ.
            Seed.FillTags(tags);
        }

        WebApplication app = builder.Build();

        // Not UseStaticFiles: MapStaticAssets serves from the build's endpoint
        // manifest, which is what fingerprints and precompresses. It is also the
        // only one of the two that can see the framework's own assets -- boot.js
        // and the design-system stylesheets are contributed by
        // libs/nativeweb.web/build/NativeWeb.Web.targets and are not physically
        // under this app's wwwroot at all, so a file-system middleware pointed at
        // that directory would 404 every one of them (spec 015 R7).
        app.MapStaticAssets();

        // The framework pipeline, in the order spec 015 R5 documents and a test
        // asserts. It covers the antiforgery half of UseScaffolding; the other
        // half is the two lines below, which stay visible here because who may
        // reach /admin is this app's decision and a default from a library is one
        // nobody reviews. They go in the pipeline's authentication slot rather
        // than after it, because antiforgery validation compares against the user
        // as it stands when that middleware runs.
        app.UseNativeWebPipeline(Locales.All, auth =>
        {
            auth.UseAuthentication();
            auth.UseAuthorization();
        });

        // Every route under /admin, for every [Scaffold] model in this assembly. This
        // one call is the whole of the admin UI's wiring.
        app.MapScaffolding();

        // The hand-written counterpart, for contrast: one GET, written out in
        // PublicArticleEndpoints.cs rather than generated from an attribute.
        app.MapPublicArticles();

        app.MapGet("/", () => Results.Redirect("/admin"));

        // /healthz, /version, /readyz, /csp-report and /.well-known/security.txt.
        // /healthz used to be a MapGet on this line; it is the framework's now, and
        // /readyz probes the same pooled data source the stores above read through.
        app.MapNativeWebEndpoints(options);

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
