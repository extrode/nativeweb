using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using Npgsql;

using NativeWeb.Permissions;
using NativeWeb.Scaffold;
using NativeWeb.Scaffold.Generated;
using NativeWeb.SignIn;
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

        // typeof(Program).Assembly, not the entry assembly: under the integration
        // tier the entry assembly is testhost, and /version would report its
        // version rather than this app's.
        builder.AddNativeWebWeb(options, typeof(Program).Assembly);

        // Antiforgery and authorization, which every scaffolded route needs.
        builder.Services.AddScaffolding();

        // Spec 024. The catalog is what this build's generator emitted from every
        // [Permissions] model, passed in rather than discovered: a static registry
        // would be shared by two hosts in one process. The store is registered below
        // with the scaffold stores, because it degrades the same way they do.
        builder.Services.AddPermissions(PermissionCatalog.All);

        // The OAuth relying party (spec 016 R28), pointed at the demo authorization
        // server this app mounts on itself below. Development only, because that
        // server is: outside Development the app has no provider configured and
        // registering a scheme against blank endpoints would fail at startup.
        //
        // Registered BEFORE the cookie scheme on purpose. AddSignIn makes itself the
        // default scheme only when it is the first one registered, and the cookie
        // call below reasserts the default after it either way -- the scaffolded
        // admin's challenge has to stay the cookie's, whatever else is mounted.
        if (builder.Environment.IsDevelopment())
        {
            string origin = DemoProvider.SelfOrigin(builder.Configuration);

            // Where the browser goes, and where this app's own HttpClient goes, which are the
            // same origin unless a test has said otherwise. See DemoProvider.BackChannelOrigin.
            string backChannel = DemoProvider.BackChannelOrigin(builder.Configuration);

            builder.Services.AddSingleton<DemoProvider>();
            builder.Services.AddSignIn(signIn =>
            {
                signIn.AuthorizationEndpoint = $"{origin}{DemoProvider.BasePath}/authorize";
                signIn.TokenEndpoint = $"{backChannel}{DemoProvider.BasePath}/token";
                signIn.UserInfoEndpoint = $"{backChannel}{DemoProvider.BasePath}/userinfo";
                signIn.IntrospectionEndpoint = $"{backChannel}{DemoProvider.BasePath}/introspect";
                signIn.ClientId = "nativeweb-sample";
                signIn.ClientSecret = "demo-secret";
                signIn.RedirectUri = $"{origin}{DemoSignInPaths.Callback}";
                signIn.LoginPath = DemoSignInPaths.Login;
                signIn.CallbackPath = DemoSignInPaths.Callback;
                signIn.LogoutPath = DemoSignInPaths.Logout;
                signIn.SignedOutPath = "/welcome";
                signIn.DefaultReturnPath = "/account";

                // Not LoginPath. That route IS the flow trigger, so a failure sent
                // there starts the flow again and fails again; the demo's welcome
                // page is a page, and a page can render ?error=.
                signIn.FailurePath = "/welcome";

                // A demo's sessions do not outlive the process that minted them, so
                // there is nothing for a persisted key to protect. A product supplies
                // one; see the library's README.
                signIn.AllowEphemeralStoreEncryptionKey = true;
                signIn.CookieName = DemoSignInPaths.SessionCookie;
            });
        }

        // The passphrase that admits an operator outside Development (spec 020). Read
        // here so Production fails at startup, not at the first /signin.
        var adminPassphrase = AdminPassphrase.FromConfiguration(
            builder.Configuration, "Sample", builder.Environment);
        builder.Services.AddAdminSignInRateLimiting();

        // Authentication is deliberately NOT part of that call: who may reach /admin
        // is this app's decision, and a default from a library is one nobody reviews.
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "nw-sample";
                options.LoginPath = "/signin";
            });

        // Which of the two schemes answers a request that named neither. See
        // DemoSignInPaths.EitherScheme for what breaks without it -- the sign-out form,
        // first and least obviously.
        if (builder.Environment.IsDevelopment())
        {
            builder.Services
                .AddAuthentication(authentication =>
                    authentication.DefaultScheme = DemoSignInPaths.EitherScheme)
                .AddPolicyScheme(
                    DemoSignInPaths.EitherScheme,
                    "The OAuth session when the visitor carries one, the cookie demo otherwise",
                    policy => policy.ForwardDefaultSelector = http =>
                        http.Request.Cookies.ContainsKey(DemoSignInPaths.SessionCookie)
                            ? SessionAuthenticationHandler.SchemeName
                            : CookieAuthenticationDefaults.AuthenticationScheme);
        }

        // The generated endpoints resolve IScaffoldStore<T> from RequestServices and
        // know nothing else about persistence. Which implementation answers is decided
        // here, once, and no generated code is regenerated when it changes.
        //
        // Same degrade-rather-than-crash posture as apps/web: a missing connection
        // string is not an error, it selects the in-memory store. That keeps
        // `dotnet run`, the integration tier and the Playwright tier working with no
        // database, while a configured string exercises the JauntyQ path against a
        // real one. Both models are IScaffoldStore<T> on both paths; nothing above
        // this line differs.
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (ArticleStore articles, TagStore tags, CategoryStore categories, MemoryRelation relation) = MemoryStores.Create();
            builder.Services.AddSingleton<IScaffoldStore<Article>>(articles);
            builder.Services.AddSingleton<IScaffoldStore<Tag>>(tags);
            builder.Services.AddSingleton<IScaffoldStore<Category>>(categories);
            Seed.Fill(articles, tags, categories, relation);

            // The public pages read through the same seams. In-memory they have to
            // filter, sort and count themselves; see MemoryPublishedArticles.
            builder.Services.AddSingleton<IPublishedArticles>(new MemoryPublishedArticles(articles, tags, relation));
            builder.Services.AddSingleton<IPublicTags>(new MemoryPublicTags(tags, articles, relation));

            // Grants live as long as the process on this path, which is what makes
            // the Playwright tier able to grant one and watch a control appear.
            //
            // ArticlePermissions.All, not PermissionCatalog.All. The catalog is every
            // permission the assembly declares; migration 0006 seeds four literal
            // sample:article.* names. Those are the same set only while Article is the
            // one model carrying [Permissions], so the catalog would have silently
            // seeded a second model's permissions here and not on the database path.
            // SeedGrantParityTests holds the two lists together.
            var permissions = new InMemoryPermissionStore();
            foreach (string subject in new[] { "operator", "demo" })
            {
                permissions.EnsureSubjectAsync(subject, default).AsTask().GetAwaiter().GetResult();
                foreach (Permission permission in ArticlePermissions.All)
                {
                    permissions.GrantToSubjectAsync(subject, permission.Name, default)
                        .AsTask().GetAwaiter().GetResult();
                }
            }

            builder.Services.AddSingleton<IPermissionStore>(permissions);
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

            // Tags persist too, from spec 022: until then they were an in-memory list
            // on this path and an admin's edit did not survive a restart. The three
            // seeded tags are rows migration 0003 inserts, so nothing seeds here.
            builder.Services.AddSingleton<IScaffoldStore<Tag>>(
                sp => new JauntyTagStore(sp.GetRequiredService<NpgsqlDataSource>()));
            builder.Services.AddSingleton<IScaffoldStore<Category>>(
                sp => new JauntyCategoryStore(sp.GetRequiredService<NpgsqlDataSource>()));

            // Same seam, but the filter and the ordering move into SQL. This is the
            // pair the tutorial in docs/00-quick-start/tutorial-first-feature.md compares.
            builder.Services.AddSingleton<IPublishedArticles>(
                sp => new JauntyPublishedArticles(sp.GetRequiredService<NpgsqlDataSource>()));
            builder.Services.AddSingleton<IPublicTags>(
                sp => new JauntyPublicTags(sp.GetRequiredService<NpgsqlDataSource>()));

            // The permission tables are migration 0006's, and the first operator's
            // grant is seeded there rather than here: on a database there is nowhere
            // for a startup grant to go that a restart would not repeat.
            builder.Services.AddSingleton<IPermissionStore>(
                sp => new JauntyPermissionStore(sp.GetRequiredService<NpgsqlDataSource>()));
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

        // Groups, members and grants, on the same chrome. The two delegates are the
        // generated ScaffoldLayout's own methods: the library cannot name that type,
        // because the generator emits it into this assembly.
        app.MapAccessAdmin("/admin", ScaffoldLayout.Page, ScaffoldLayout.AntiforgeryField)
            .RequireAuthorization(policy => policy.RequireRole("admin"));

        // The hand-written counterpart, for contrast: one GET, written out in
        // PublicArticleEndpoints.cs rather than generated from an attribute.
        app.MapPublicArticles();

        // The feed, sitemap, robots file and one plain document (spec 021):
        // the same articles as Atom, RSS, JSON and CSV beside the page above.
        app.MapPublicDocuments();

        // The site itself (spec 022): home, one page per article, the tags. Until
        // then / redirected to /admin, which made the editor the front door.
        app.MapPublicSite();

        // /healthz, /version, /readyz, /csp-report and /.well-known/security.txt.
        // /healthz used to be a MapGet on this line; it is the framework's now, and
        // /readyz probes the same pooled data source the stores above read through.
        app.MapNativeWebEndpoints(options, typeof(Program).Assembly);

        // Demo sign-in. Gated to Development because it grants the admin role for the
        // asking, which is only acceptable in something nobody deploys.
        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/signin", async (HttpContext http) =>
            {
                // The subject the permission layer reads, matching what migration
                // 0006 seeds and what the in-memory path grants above.
                var identity = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Name, "demo"),
                        new Claim(ClaimTypes.NameIdentifier, "demo"),
                        new Claim(ClaimTypes.Role, "admin"),
                    ],
                    CookieAuthenticationDefaults.AuthenticationScheme);

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity)).ConfigureAwait(false);

                return Results.Redirect("/admin");
            });

            // The sign-in demo: the relying party's three endpoints, the authorization
            // server they talk to, and one page on each side of the line (R28).
            app.MapSignIn();
            app.MapDemoProvider();
            app.MapDemoSignIn();
        }
        else
        {
            // Everywhere else the admin is behind the passphrase (spec 020): the
            // form, the POST it submits to, and the one sign-out the sample has.
            app.MapAdminSignIn(adminPassphrase);
        }

        app.Run();
    }
}

internal static class Seed
{
    // The same three tags migration 0003 seeds on the database path, and the same
    // eight articles migration 0005 seeds there -- one dataset written twice, so
    // a run picks a store and not a different site. A row changed here is changed
    // there; 0005's header says what each of the eight is for.
    //
    // a1..a3 keep spec 022 R4's shape exactly: one article with two tags, one
    // with none, and one tag (postgres) whose only published article arrived in
    // a5, so the tag listing still has the zero-then-one case and /tags/postgres
    // still proves a draft is invisible.
    internal static void Fill(ArticleStore articles, TagStore tags, CategoryStore categories, MemoryRelation relation)
    {
        categories.Seed(new Category { Key = "guides", Name = "Guides" });
        categories.Seed(new Category { Key = "notes", Name = "Notes" });
        tags.Seed(new Tag { Slug = "aot", Name = "NativeAOT", Color = "#5bbd83" });
        tags.Seed(new Tag { Slug = "htmx", Name = "HTMX", Color = "#7aa2f7" });
        tags.Seed(new Tag { Slug = "postgres", Name = "PostgreSQL", Color = "#336791" });
        relation.Link("a1", "aot");
        relation.Link("a1", "htmx");
        relation.Link("a3", "postgres");
        relation.Link("a4", "htmx");
        relation.Link("a5", "aot");
        relation.Link("a7", "aot");
        relation.Link("a8", "aot");
        relation.Link("a8", "htmx");
        articles.Seed(new Article
        {
            Id = "a1",
            Title = "Lean Native Web",
            Slug = "lean-native-web",
            Author = "Sy",
            Body = "Server-rendered HTML, one native binary, no client build step.",
            Status = ArticleStatus.Published,
            PublishedAt = new DateOnly(2026, 3, 5),
            CategoryKey = "guides",
            ViewCount = 1420,
            CreatedAt = new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a2",
            Title = "Binding forms under NativeAOT",
            Slug = "binding-forms-under-nativeaot",
            Author = "Sy",
            Body = "Complex-type [FromForm] does not compile. IBindableFromHttpContext does.",
            Status = ArticleStatus.Published,
            PublishedAt = new DateOnly(2026, 6, 20),
            CategoryKey = "notes",
            ViewCount = 388,
            CreatedAt = new DateTime(2026, 6, 18, 14, 5, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a3",
            Title = "Scaffolding without reflection",
            Slug = "scaffolding-without-reflection",
            Author = null,
            Body = "Draft.",
            Status = ArticleStatus.Draft,
            ViewCount = 0,
            CreatedAt = new DateTime(2026, 8, 19, 8, 0, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a4",
            Title = "The template language we removed",
            Slug = "the-template-language-we-removed",
            Author = "Sy",
            Body = "An earlier cut of the renderer had loops, filters and partial inheritance. It was archived when the third nested conditional turned out to be a C# method wearing a costume.\n\nWhat survives is smaller: parameters, a slot, a partial, and an if. Anything that wants more logic than that has a compiler available to it.",
            Status = ArticleStatus.Archived,
            PublishedAt = new DateOnly(2026, 4, 12),
            CategoryKey = "notes",
            ViewCount = 97,
            CreatedAt = new DateTime(2026, 4, 11, 11, 20, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a5",
            Title = "Reading an EXPLAIN before adding an index",
            Slug = "reading-an-explain-before-adding-an-index",
            Author = "Dana Whitfield",
            Body = "A keyset page over a nullable column lands in Filter rather than Index Cond, because a disjunction over IS NULL is not a rangeable start point.\n\nThe fix is not a bigger index. It is knowing which columns are worth paging by, and saying so where the model is declared.",
            Status = ArticleStatus.Published,
            PublishedAt = new DateOnly(2026, 7, 3),
            CategoryKey = "guides",
            ViewCount = 512,
            CreatedAt = new DateTime(2026, 7, 2, 16, 45, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a6",
            Title = "Notes from a week without a bundler",
            Slug = "notes-from-a-week-without-a-bundler",
            Author = "Dana Whitfield",
            Body = "No package.json, no lockfile, no node_modules, and a page that loads in one round trip. The vendored libraries are two files and a version comment.",
            Status = ArticleStatus.Published,
            PublishedAt = new DateOnly(2026, 7, 29),
            CategoryKey = null,
            ViewCount = 264,
            CreatedAt = new DateTime(2026, 7, 28, 10, 15, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a7",
            Title = "What a source generator owes you",
            Slug = "what-a-source-generator-owes-you",
            Author = "Sy",
            Body = string.Join(
                "\n\n",
                "Reflection is convenient at the cost of a startup you cannot see and a trim warning you cannot fix. A generator moves both into the build, where they are yours to read.",
                "The first thing it owes you is legible output. Generated code that no one opens is a second compiler nobody asked for, and when it is wrong the error arrives in a file the author has never seen.",
                "The second is a diagnostic instead of a cascade. A model the generator refuses should say why, once, at the declaration -- not produce eleven CS0103s about a class it declined to write.",
                "The third is determinism. The same sources produce the same output, in the same order, or the build stops being a fact and becomes a mood.",
                "The fourth is a way out. Every generated member should be reachable by hand-written code, and nothing should be reachable only by generated code.",
                "None of that is free. The cost is that the generator is now a compiler you maintain, with its own tests, its own diagnostics and its own backwards-compatibility problem.",
                "The trade is worth making where the alternative is reflection over types the trimmer is trying to remove. It is not worth making to save typing.",
                "Which is the whole argument: a generator earns its place by removing a runtime cost, not by removing a keystroke."),
            Status = ArticleStatus.Published,
            PublishedAt = new DateOnly(2026, 8, 6),
            CategoryKey = "guides",
            ViewCount = 1183,
            CreatedAt = new DateTime(2026, 8, 5, 13, 0, 0, DateTimeKind.Utc),
        });

        articles.Seed(new Article
        {
            Id = "a8",
            Title = "A note written last week and published today",
            Slug = "a-note-written-last-week-and-published-today",
            Author = "Sy",
            Body = "Backdated on purpose. The public order is the publication date, and this row sits where that date puts it rather than where its creation time would.",
            Status = ArticleStatus.Published,
            PublishedAt = new DateOnly(2026, 8, 24),
            CategoryKey = "notes",
            ViewCount = 41,
            CreatedAt = new DateTime(2026, 9, 2, 7, 10, 0, DateTimeKind.Utc),
        });

    }
}
