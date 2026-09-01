using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NativeWeb.Scaffold.Generator;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// Drives the scaffold generator directly and asserts on what it emits.
/// </summary>
/// <remarks>
/// Asserting on emitted source rather than on runtime behaviour is the right layer
/// for the guarantees that matter here: that a read-only property has no input, and
/// that every mutating route carries the antiforgery metadata. Both are properties
/// of every model the generator will ever see, and an integration test can only
/// check the one model it happens to have.
/// </remarks>
public sealed class ScaffoldGeneratorTests
{
    // Declared inline rather than referenced, so the test compilation needs nothing
    // but the BCL. The generator matches by metadata name, not by assembly identity.
    private const string Attributes = """
        namespace NativeWeb.Scaffold
        {
            public sealed class ScaffoldAttribute : System.Attribute
            {
                public string? Display { get; init; }
                public string? Route { get; init; }
            }

            public sealed class AuthAttribute : System.Attribute
            {
                public string? Role { get; init; }
                public string? Policy { get; init; }
                public string? Read { get; init; }
                public string? Write { get; init; }
                public string? Delete { get; init; }
            }

            public sealed class ScaffoldListAttribute : System.Attribute { public string? Label { get; init; } }
            public sealed class ScaffoldSearchAttribute : System.Attribute { }
            public sealed class ScaffoldMultilineAttribute : System.Attribute { }
            public sealed class ScaffoldReadOnlyAttribute : System.Attribute { }
            public sealed class ScaffoldKeyAttribute : System.Attribute { }
            public sealed class ScaffoldSortAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class ScaffoldRootAttribute : System.Attribute
            {
                public ScaffoldRootAttribute(string path) => Path = path;
                public string Path { get; }
            }
        }
        """;

    private const string ArticleModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold(Display = "Articles", Route = "articles")]
            [Auth(Role = "admin", Delete = "owner")]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList][ScaffoldSearch] public string Title { get; set; } = "";
                [ScaffoldMultiline] public string Body { get; set; } = "";
                [ScaffoldList] public bool Published { get; set; }
                [ScaffoldList][ScaffoldReadOnly] public System.DateTime CreatedAt { get; set; }
            }
        }
        """;

    private static List<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

    private static (ImmutableArray<Diagnostic> Diagnostics, string Source) Run(params string[] sources)
    {
        List<MetadataReference> references = PlatformReferences();

        var compilation = CSharpCompilation.Create(
            "scaffold-generator-tests",
            syntaxTrees: sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new ScaffoldGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        string emitted = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

        return (diagnostics, emitted);
    }

    // Every scalar type the emitter claims to support, on one model. Compiled for
    // real rather than pattern-matched, because the defect this exists for was that
    // the emitted C# did not build: short and float fell through the conversion
    // table to the string case and produced `short Quantity = ScaffoldValues.Text(
    // form.Quantity) ?? string.Empty` -- CS0029. No assertion about the shape of
    // the generated source could have caught that; only the compiler could.
    private const string EveryTypeModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold]
            public sealed class Everything
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                public string Text { get; set; } = "";
                public string? MaybeText { get; set; }
                public short Small { get; set; }
                public short? MaybeSmall { get; set; }
                public int Whole { get; set; }
                public int? MaybeWhole { get; set; }
                public long Big { get; set; }
                public long? MaybeBig { get; set; }
                public decimal Money { get; set; }
                public decimal? MaybeMoney { get; set; }
                public double Real { get; set; }
                public double? MaybeReal { get; set; }
                public float Approx { get; set; }
                public float? MaybeApprox { get; set; }
                public bool Flag { get; set; }
                public bool? MaybeFlag { get; set; }
                public System.DateTime When { get; set; }
                public System.DateTime? MaybeWhen { get; set; }
                public System.Guid Ident { get; set; }
                public System.Guid? MaybeIdent { get; set; }
            }
        }
        """;

    // The shapes an ordinary model can have that no conversion table covers: value
    // types outside it, an enum, a computed property, an indexer, and a setter the
    // generated code cannot reach. Each one used to emit an assignment -- CS0029 for
    // the types, CS0200 for the unsettable ones.
    private const string AwkwardModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            public enum Rank { Low, High }

            [Scaffold]
            public sealed class Awkward
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                public string Title { get; set; } = "";
                public string Summary => Title + "!";
                public byte Level { get; set; }
                public ushort Port { get; set; }
                public uint Count { get; set; }
                public sbyte Offset { get; set; }
                public char Initial { get; set; }
                public ulong Huge { get; set; }
                public Rank Grade { get; set; }
                public Rank? MaybeGrade { get; set; }
                public System.DateOnly Day { get; set; }
                public System.TimeOnly Hour { get; set; }
                public System.DateTimeOffset Moment { get; set; }
                public System.TimeSpan Duration { get; set; }
                public string Locked { get; private set; } = "";
                public string this[int i] => Title;
            }
        }
        """;

    [Fact]
    public void EverySupportedPropertyType_EmitsCodeThatCompiles() =>
        AssertGeneratedSourceCompiles(EveryTypeModel);

    [Fact]
    public void TypesWithNoConversion_AndPropertiesWithNoReachableSetter_EmitCodeThatCompiles() =>
        AssertGeneratedSourceCompiles(AwkwardModel);

    // The version this repository and every consumer of it build at. Directory.Build.props
    // sets LangVersion=latest against net10.0, and that is the whole policy: NativeWeb
    // tracks the newest C# the current SDK ships and pins nothing below it.
    //
    // This theory used to assert a C# 11 floor -- that the emitted source stayed
    // compilable for a consumer who pinned an older LangVersion. Removed 2026-08-31:
    // no such promise is made, and a test that guards an unmade promise turns red for
    // a reason nobody has to act on. What survives is the mechanism underneath it,
    // which was a real defect: the parse options now reach the generator driver, so
    // the emitted trees are compiled at the version asked for rather than at the
    // default whatever the caller set.
    [Theory]
    [InlineData(LanguageVersion.Latest)]
    [InlineData(LanguageVersion.Preview)]
    public void EmittedCode_CompilesAtTheLanguageVersionThisRepositoryBuildsAt(LanguageVersion language)
    {
        AssertGeneratedSourceCompiles(EveryTypeModel, language);
        AssertGeneratedSourceCompiles(AwkwardModel, language);
    }

    [Fact]
    public void APropertyWithNoConversion_IsCarriedThroughRatherThanBoundFromTheForm()
    {
        (_, string emitted) = Run(AwkwardModel);

        Assert.Contains("Grade = existing is null ? default! : existing.Grade,", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Summary =", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Locked =", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("this[]", emitted, StringComparison.Ordinal);
    }

    // The real NativeWeb.Scaffold attributes, not the inline copies the other tests
    // declare: this compilation has to resolve ScaffoldValues and ScaffoldEndpoints
    // too, and two definitions of one attribute name would be ambiguous rather than
    // convenient.
    private static void AssertGeneratedSourceCompiles(string model, LanguageVersion language = LanguageVersion.Default)
    {
        CSharpParseOptions parseOptions = new(language);

        var compilation = CSharpCompilation.Create(
            "scaffold-generator-compile-test",
            syntaxTrees: [CSharpSyntaxTree.ParseText(model, parseOptions)],
            references: PlatformReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // The parse options travel to the driver as well as to the model tree. Without
        // them the generated sources are parsed at the default language version
        // whatever the consumer set, so the gate below would compile the emitted code
        // under rules no consumer is bound by.
        GeneratorDriver driver = CSharpGeneratorDriver
            .Create([new ScaffoldGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        // Without this the test passes when the generator THROWS: an exception in a
        // generator is reported as CS8785 on the driver and leaves the compilation
        // with nothing added, so "the generated source has no errors" is satisfied
        // by there being no generated source. Measured by mutation, 2026-08-31.
        GeneratorRunResult result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.NotEmpty(result.GeneratedSources);

        Diagnostic[] errors = updated
            .GetDiagnostics()
            .Concat(driver.GetRunResult().Diagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            "Generated source did not compile:\n" + string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void ModelWithNoKey_ReportsNws001()
    {
        const string noKey = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Widget
                {
                    public string Name { get; set; } = "";
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Attributes, noKey);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWS001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void WellFormedModel_ReportsNothing()
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Attributes, ArticleModel);

        Assert.Empty(diagnostics);
    }

    // The mass-assignment guarantee. The form record IS the allowlist: a property
    // that is not a constructor parameter of it cannot be bound by any request,
    // because there is no code path that reads it.
    [Fact]
    public void FormRecord_OmitsTheKeyAndEveryReadOnlyProperty()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("public sealed record ArticleForm(", source, StringComparison.Ordinal);
        Assert.Contains("string? Title", source, StringComparison.Ordinal);
        Assert.Contains("string? Body", source, StringComparison.Ordinal);
        Assert.Contains("string? Published", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string? Id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string? CreatedAt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("form[\"id\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("form[\"createdAt\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyProperties_AreCarriedFromTheExistingRowNotTheForm()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("CreatedAt = existing is null ? default! : existing.CreatedAt", source, StringComparison.Ordinal);
        Assert.Contains("Id = existing is null ? default! : existing.Id", source, StringComparison.Ordinal);
    }

    // Measured in brainstorm 001 sec.12: a BindAsync-bound handler carries no
    // antiforgery metadata of its own, so UseAntiforgery() skips it and a tokenless
    // POST is answered 200 OK. Every mutating route has to re-attach it, and the
    // reason to assert on the count is that "most of them" is the failure mode.
    [Fact]
    public void EveryMutatingRoute_CarriesAntiforgeryMetadata()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        int posts = Occurrences(source, "group.MapPost(");
        int guards = Occurrences(source, ".WithMetadata(RequiresAntiforgery.Instance)");

        Assert.Equal(3, posts);
        Assert.Equal(posts, guards);
    }

    // Binding runs before the endpoint-filter pipeline, so the content-type check
    // cannot live in a filter -- it has to be inside BindAsync or it does not run.
    [Fact]
    public void Binder_RejectsANonFormBodyItself()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("if (!context.Request.HasFormContentType)", source, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status415UnsupportedMediaType", source, StringComparison.Ordinal);
    }

    // Measured 2026-08-19: a NativeAOT publish failed IL2026/IL3050 on every
    // generated Map* call. The Request Delegate Generator only rewrites Map* calls
    // in the compilation's own syntax trees, and Roslyn does not feed one
    // generator's output to another -- so an emitted Map* always binds the
    // reflection-based Delegate overload. The RequestDelegate overloads carry
    // neither attribute. Every route must go through ScaffoldEndpoints.Handle.
    [Fact]
    public void EveryRoute_IsMappedAsARequestDelegateSoTheAotPublishStaysClean()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        // Six per model plus the admin index. Counting rather than pattern-matching
        // each route, because the failure mode is one route being missed.
        int routes = Occurrences(source, ".MapGet(") + Occurrences(source, ".MapPost(");
        Assert.Equal(7, routes);
        Assert.Equal(routes, Occurrences(source, "ScaffoldEndpoints.Handle("));
    }

    [Fact]
    public void RouteParameters_AreReadFromRouteValuesRatherThanBound()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("ScaffoldEndpoints.RouteValue(http, \"id\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewPage_IsEmittedAsASynchronousPageHandler()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("private static IResult NewPage(HttpContext http)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PerOperationRoles_OverrideTheModelWideRole()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("Roles = \"admin\"", source, StringComparison.Ordinal);
        Assert.Contains("Roles = \"owner\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelWithNoAuthAttribute_EmitsNoAuthorizationCall()
    {
        const string open = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Tag
                {
                    [ScaffoldKey] public string Slug { get; set; } = "";
                    [ScaffoldList] public string Name { get; set; } = "";
                }
            }
            """;

        (_, string source) = Run(Attributes, open);

        Assert.Contains("MapTagScaffold", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthorization", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteAndDisplay_FallBackToThePluralisedTypeName()
    {
        const string bare = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Category
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    [ScaffoldList] public string Name { get; set; } = "";
                }
            }
            """;

        (_, string source) = Run(Attributes, bare);

        Assert.Contains("BasePath = \"/admin/categories\"", source, StringComparison.Ordinal);
        Assert.Contains("Display = \"Categories\"", source, StringComparison.Ordinal);
        Assert.Contains("Singular = \"Category\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmittedSource_CompilesWithNoErrors()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "scaffold-emit-tests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(Attributes), CSharpSyntaxTree.ParseText(ArticleModel)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create(new ScaffoldGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        // Only syntax is checked here: the test compilation deliberately has no
        // ASP.NET reference, so every unresolved framework symbol would be noise.
        // That the whole thing binds is proven by apps/sample building at all.
        Assert.DoesNotContain(
            updated.SyntaxTrees.SelectMany(t => t.GetDiagnostics()),
            d => d.Severity == DiagnosticSeverity.Error);
    }

    // The scaffold root. Hardcoding "/admin" was the original shape, so these guard
    // the whole surface it leaked into: the route group, every link in the index,
    // the page header, and the per-model base path the forms and redirects use.
    [Fact]
    public void ScaffoldRoot_DefaultsToAdmin()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("private const string BasePath = \"/admin/articles\";", source, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapGet(\"/admin\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldRoot_MovesEveryEmittedPath()
    {
        const string root = """[assembly: NativeWeb.Scaffold.ScaffoldRoot("/manage")]""";

        (ImmutableArray<Diagnostic> diagnostics, string source) = Run(Attributes, root, ArticleModel);

        Assert.Empty(diagnostics);
        Assert.Contains("private const string BasePath = \"/manage/articles\";", source, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapGet(\"/manage\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\\\"/manage/articles\\\"", source, StringComparison.Ordinal);

        // The point of the test: not one path may be left behind on the old root.
        Assert.DoesNotContain("/admin", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldRoot_TrimsATrailingSlash()
    {
        const string root = """[assembly: NativeWeb.Scaffold.ScaffoldRoot("/manage/")]""";

        (_, string source) = Run(Attributes, root, ArticleModel);

        Assert.Contains("private const string BasePath = \"/manage/articles\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("//articles", source, StringComparison.Ordinal);
    }

    // "/" is legal and means the site root, where the index is "/" and a model sits
    // directly at "/articles". The empty-string normalisation must not leak out.
    [Fact]
    public void ScaffoldRoot_SupportsTheSiteRoot()
    {
        const string root = """[assembly: NativeWeb.Scaffold.ScaffoldRoot("/")]""";

        (ImmutableArray<Diagnostic> diagnostics, string source) = Run(Attributes, root, ArticleModel);

        Assert.Empty(diagnostics);
        Assert.Contains("private const string BasePath = \"/articles\";", source, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapGet(\"/\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData("/admin?x=1")]
    [InlineData("/two words")]
    public void ScaffoldRoot_ThatIsNotAPath_ReportsNws002(string bad)
    {
        string root = $"[assembly: NativeWeb.Scaffold.ScaffoldRoot(\"{bad}\")]";

        (ImmutableArray<Diagnostic> diagnostics, string source) = Run(Attributes, root, ArticleModel);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWS002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // Nothing is emitted on a bad root, so a typo cannot silently publish an
        // admin UI at a path the author did not choose.
        Assert.Equal(string.Empty, source);
    }

    // --- T11, the scaffold defect batch. ---

    private const string OpenTagModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold]
            public sealed class Tag
            {
                [ScaffoldKey] public string Slug { get; set; } = "";
                [ScaffoldList][ScaffoldSearch] public string Name { get; set; } = "";
            }
        }
        """;

    // The index listed every model to everyone, so a guarded model's name and URL
    // were disclosed to a caller who would then be refused at the link.
    [Fact]
    public void AdminIndex_GatesAGuardedModelsLinkOnTheReadRequirement()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains(
            "if (await ScaffoldAccess.CanReadAsync(http, null, \"admin\", true).ConfigureAwait(false))",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AdminIndex_LeavesAnOpenModelsLinkUnconditional()
    {
        (_, string source) = Run(Attributes, OpenTagModel);

        Assert.Contains("<li><a href=\\\"/admin/tags\\\">Tags</a></li>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaffoldAccess.CanReadAsync", source, StringComparison.Ordinal);
    }

    // Metadata as well as filtering, and the two answer different questions: the
    // filter decides what an allowed caller sees, this decides whether an anonymous
    // one gets a page at all. Only when EVERY model is guarded -- one open model
    // makes the index legitimately anonymous, and the filter is what protects the
    // rest.
    [Fact]
    public void AdminIndex_RequiresAnIdentityOnlyWhenEveryModelDoes()
    {
        (_, string guarded) = Run(Attributes, ArticleModel);
        Assert.Contains("})).RequireAuthorization();", guarded, StringComparison.Ordinal);

        (_, string mixed) = Run(Attributes, ArticleModel, OpenTagModel);
        Assert.DoesNotContain("})).RequireAuthorization();", mixed, StringComparison.Ordinal);
    }

    // [ScaffoldSearch] reached the search box's placeholder text and nothing else.
    // Every store re-declared the searchable columns by hand, and the two lists
    // drifted with nothing to notice.
    [Fact]
    public void SearchableProperties_AreHandedToTheStoreOnEveryList()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("public static readonly string[] SearchFields = new string[] { \"Title\" };", source, StringComparison.Ordinal);
        Assert.Contains(
            "store.ListAsync(search, SearchFields, sort, SortFields, after, PageSize + 1, ct)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("store.CountAsync(search, SearchFields,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelWithNothingSearchable_HandsDownAnEmptyList()
    {
        const string plain = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Note
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    [ScaffoldList] public string Body { get; set; } = "";
                }
            }
            """;

        (_, string source) = Run(Attributes, plain);

        Assert.Contains("SearchFields = new string[] {  };", source, StringComparison.Ordinal);
    }

    // A discarded false reported a deletion that did not happen and an edit that was
    // not saved. The update case is the concurrency one: the row was there when
    // GetAsync ran and gone by the time UpdateAsync did.
    [Fact]
    public void DeleteAndUpdate_AnswerFalseWithNotFound()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        // The branch BODY, not only that the boolean is read. Both assertions used
        // to stop at the `if`, so a branch that redirected to the list -- the very
        // thing the fix removed -- passed.
        AssertBranchBody(
            source,
            "if (!await store.DeleteAsync(id, ct).ConfigureAwait(false))",
            "return Results.NotFound();");

        AssertBranchBody(
            source,
            "if (!await store.UpdateAsync(id, item, ct).ConfigureAwait(false))",
            "return Results.NotFound();");
    }

    private static void AssertBranchBody(string source, string condition, string expected)
    {
        int start = source.IndexOf(condition, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the emitted source has no `{condition}`.");

        int close = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(close > start, $"`{condition}` has no closing brace.");

        Assert.Contains(expected, source[start..close], StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RedirectsToTheKeyTheStoreReturned()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("string created = await store.CreateAsync(item, ct)", source, StringComparison.Ordinal);
        Assert.Contains("Results.Redirect(BasePath + \"/\" + Uri.EscapeDataString(created))", source, StringComparison.Ordinal);
    }

    // An empty key would redirect to BasePath + "/", which routes to the list -- so a
    // store that forgot to return its key produced a create that looked successful
    // and a row nobody could reach. The contract says the store assigns the key; a
    // store that breaks it is told so rather than being papered over.
    [Fact]
    public void Create_WithNoKeyBack_FailsInsteadOfRedirectingToTheList()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("if (string.IsNullOrEmpty(created))", source, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(", source, StringComparison.Ordinal);
    }

    // The rejected form is rebuilt from what was submitted, not from the converted
    // item: the fallback the converter chose is what the validator just refused, so
    // showing it back is showing the visitor a value they did not type.
    [Fact]
    public void ARejectedForm_IsRedisplayedFromTheSubmittedValues()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("ArticleForm? submitted = null)", source, StringComparison.Ordinal);
        Assert.Contains("submitted is not null ? (submitted.Title ?? \"\")", source, StringComparison.Ordinal);

        // And a rejected create is still a create: no row, so no "Edit" heading and
        // the action stays the collection.
        Assert.Contains("FormHtml(http, null, BasePath, \"Create\", error, form)", source, StringComparison.Ordinal);
    }

    // This used to assert a clamp on ?page=, because (page - 1) * PageSize was
    // unchecked int arithmetic and a page number in the hundreds of millions landed on
    // a negative offset, from an anonymous GET, with the query string as the whole
    // attack. Spec 017 replaced offset paging with a cursor, which cannot express an
    // offset at all -- so the guard is gone and what is asserted now is that nothing
    // computes one. The clamp was correct; having no arithmetic to clamp is better.
    [Fact]
    public void TheListPage_ComputesNoOffset()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.DoesNotContain("MaxPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Query[\"page\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("* PageSize", source, StringComparison.Ordinal);
    }

    private const string SortableModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold(Route = "articles")]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList][ScaffoldSort] public string Title { get; set; } = "";
                [ScaffoldList][ScaffoldSort] public string? Author { get; set; }
                [ScaffoldList] public bool Published { get; set; }
                [ScaffoldSort] public System.DateTime CreatedAt { get; set; }
            }
        }
        """;

    [Fact]
    public void OnlyDeclaredSortableProperties_ReachTheAllowList()
    {
        (_, string source) = Run(Attributes, SortableModel);

        Assert.Contains(
            "public static readonly string[] SortFields = new string[] { \"Id\", \"Title\", \"Author\", \"CreatedAt\" };",
            source,
            StringComparison.Ordinal);
        Assert.Contains("private const string DefaultSortField = \"Id\";", source, StringComparison.Ordinal);
    }

    // The key is sortable whether or not the model says so, because it is the
    // tiebreaker every other order ends in: an allow-list without it has no name for
    // the default order to resolve to.
    [Fact]
    public void TheKey_IsSortableWithoutBeingDeclared()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("SortFields = new string[] { \"Id\" };", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AListedColumnThatIsNotSortable_GetsNoSortLink()
    {
        (_, string source) = Run(Attributes, SortableModel);

        Assert.Contains("ScaffoldLayout.Header(\"Title\", \"Title\", BasePath, search, sort)", source, StringComparison.Ordinal);
        Assert.Contains("ScaffoldLayout.Header(\"Author\", \"Author\", BasePath, search, sort)", source, StringComparison.Ordinal);
        Assert.Contains("ScaffoldLayout.Header(\"Published\", null, BasePath, search, sort)", source, StringComparison.Ordinal);
    }

    // A column can be sortable without being shown, and a sortable column that is not
    // listed still needs its value reachable for the cursor -- so the switch is over
    // the sortable set, not the listed one.
    [Fact]
    public void TheCursorsSortValue_IsASwitchOverEverySortableProperty()
    {
        (_, string source) = Run(Attributes, SortableModel);

        Assert.Contains("private static string? SortTextOf(Demo.Article row, string property)", source, StringComparison.Ordinal);
        Assert.Contains("return ScaffoldValues.CursorText(row.CreatedAt);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("row.Published);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSearchForm_CarriesTheOrderingForward()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("ScaffoldLayout.SortFields(sort)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSortableModel_EmitsCodeThatCompiles() =>
        AssertGeneratedSourceCompiles(SortableModel);

    // Malformed input is shown to the user rather than absorbed. Blank is not
    // malformed -- the converters' documented fallback still applies -- so a
    // nullable field gets no required check and no validator gets one either.
    [Fact]
    public void MalformedNonStringInput_IsRejectedBeforeItIsConverted()
    {
        const string typed = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Reading
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    public int Count { get; set; }
                    public double? Ratio { get; set; }
                    public System.Guid Sensor { get; set; }
                    public System.DateTime TakenAt { get; set; }
                    public bool Flagged { get; set; }
                    public string Note { get; set; } = "";
                }
            }
            """;

        (_, string source) = Run(Attributes, typed);

        Assert.Contains("if (!ScaffoldValues.IsInt32(form.Count))", source, StringComparison.Ordinal);
        Assert.Contains("\"Count must be a whole number.\"", source, StringComparison.Ordinal);
        Assert.Contains("if (!ScaffoldValues.IsDouble(form.Ratio))", source, StringComparison.Ordinal);
        Assert.Contains("if (!ScaffoldValues.IsGuid(form.Sensor))", source, StringComparison.Ordinal);
        Assert.Contains("if (!ScaffoldValues.IsDateTime(form.TakenAt))", source, StringComparison.Ordinal);

        // A checkbox that is not ticked submits nothing, and a string cannot be
        // malformed. Neither gets a validator, and the non-nullable string keeps the
        // required check it already had.
        Assert.DoesNotContain("ScaffoldValues.IsBool", source, StringComparison.Ordinal);
        Assert.Contains("if (ScaffoldValues.Text(form.Note) is null)", source, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
