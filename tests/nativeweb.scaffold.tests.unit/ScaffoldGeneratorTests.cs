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
            public sealed class ScaffoldReferenceAttribute : System.Attribute
            {
                public ScaffoldReferenceAttribute(System.Type target) => Target = target;
                public System.Type Target { get; }
            }
            public sealed class ScaffoldRelationAttribute : System.Attribute
            {
                public ScaffoldRelationAttribute(System.Type target) => Target = target;
                public System.Type Target { get; }
                public ScaffoldRelationControl Control { get; init; }
            }
            public enum ScaffoldRelationControl { Checkboxes, MultiSelect }

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
    // types outside it, a computed property, an indexer, and a setter the
    // generated code cannot reach. Each one used to emit an assignment -- CS0029 for
    // the types, CS0200 for the unsettable ones. The enum and the DateOnly were on
    // this list until spec 023 gave them controls; they stay so the compile gate
    // covers them beside the types that still have none.
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

        Assert.Contains("Level = existing is null ? default! : existing.Level,", emitted, StringComparison.Ordinal);
        Assert.Contains("Hour = existing is null ? default! : existing.Hour,", emitted, StringComparison.Ordinal);
        Assert.Contains("Grade = ScaffoldValues.EnumValue<Demo.Rank>(form.Grade, default(Demo.Rank)),", emitted, StringComparison.Ordinal);
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
    public void NewPage_IsAnAsyncHandlerBecauseItLoadsTheChoices()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("private static async Task<IResult> NewAsync(HttpContext http, CancellationToken ct)", source, StringComparison.Ordinal);
        Assert.Contains("ScaffoldEndpoints.Handle(http => NewAsync(http, http.RequestAborted))", source, StringComparison.Ordinal);
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
            "if (!updated)",
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

        Assert.Contains("updated = await store.UpdateAsync(id, item, ct).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("created = await store.CreateAsync(item, ct)", source, StringComparison.Ordinal);
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
        Assert.Contains("FormHtml(http, null, BasePath, \"Create\", error, await ChoicesAsync(http, ct).ConfigureAwait(false), form)", source, StringComparison.Ordinal);
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

    [Fact]
    public void CreateAndUpdate_CatchAStoreRefusalAndReRenderTheForm_AndNothingElse()
    {
        (_, string source) = Run(AwkwardModel);

        Assert.Equal(2, Occurrences(source, "catch (ScaffoldRefusedException refused)"));
        Assert.Contains("FormHtml(http, null, BasePath, \"Create\", refused.Message, await ChoicesAsync(http, ct).ConfigureAwait(false), form)", source, StringComparison.Ordinal);
        Assert.Contains("\"Save\", refused.Message, await ChoicesAsync(http, ct).ConfigureAwait(false), form)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (Exception", source, StringComparison.Ordinal);
    }

    // The four field kinds spec 023 added, on three models that point at each
    // other: an enum, a date-only, a reference and a relation on each side of one
    // pair, one per control.
    private const string RelationModel = """
        using System.Collections.Generic;
        using NativeWeb.Scaffold;

        namespace Demo
        {
            public enum Status { Draft, InReview, Published }

            [Scaffold]
            public sealed class Category
            {
                [ScaffoldKey] public string Key { get; set; } = "";
                [ScaffoldList][ScaffoldSort] public string Name { get; set; } = "";
            }

            [Scaffold]
            public sealed class Tag
            {
                [ScaffoldKey] public string Slug { get; set; } = "";
                [ScaffoldList] public string Name { get; set; } = "";
                [ScaffoldRelation(typeof(Article), Control = ScaffoldRelationControl.MultiSelect)]
                public IReadOnlyList<string> ArticleIds { get; set; } = new string[0];
            }

            [Scaffold]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList] public string Title { get; set; } = "";
                [ScaffoldList] public Status Status { get; set; }
                public Status? Mood { get; set; }
                [ScaffoldList] public System.DateOnly? PublishedAt { get; set; }
                public System.DateOnly Day { get; set; }
                [ScaffoldList][ScaffoldReference(typeof(Category))] public string? CategoryKey { get; set; }
                [ScaffoldRelation(typeof(Tag))] public IReadOnlyList<string> TagSlugs { get; set; } = new string[0];
            }
        }
        """;

    [Fact]
    public void RelationModel_EmitsCodeThatCompiles() =>
        AssertGeneratedSourceCompiles(RelationModel);

    [Fact]
    public void ARelation_BindsAsAnArrayAndRendersTheDeclaredControl()
    {
        (ImmutableArray<Diagnostic> diagnostics, string source) = Run(Attributes, RelationModel);

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("NWS", StringComparison.Ordinal));
        Assert.Contains("string?[] TagSlugs", source, StringComparison.Ordinal);
        Assert.Contains("form[\"tagSlugs\"].ToArray()", source, StringComparison.Ordinal);
        Assert.Contains("TagSlugs = ScaffoldValues.Keys(form.TagSlugs),", source, StringComparison.Ordinal);
        Assert.Contains("<fieldset class=\\\"nws-choices\\\" id=\\\"tagSlugs\\\"><legend>Tag Slugs</legend>", source, StringComparison.Ordinal);
        Assert.Contains("<input type=\\\"checkbox\\\" name=\\\"tagSlugs\\\" value=", source, StringComparison.Ordinal);
        Assert.Contains("<select multiple id=\\\"articleIds\\\" name=\\\"articleIds\\\"", source, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<Demo.Tag> TagSlugs", source, StringComparison.Ordinal);
        Assert.Contains("public const int OptionLimit = 500;", source, StringComparison.Ordinal);
        Assert.Contains(".ListAsync(null, Array.Empty<string>(), null, TagScaffold.SortFields, null, OptionLimit, ct)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeldKeyThatIsNotOffered_IsStillRenderedTicked()
    {
        (_, string source) = Run(Attributes, RelationModel);

        Assert.Contains("if (tagSlugsSeen.Add(key))", source, StringComparison.Ordinal);
        Assert.Contains("if (!categoryKeySeen)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AReference_RendersADropdownWithANoneOptionOrderedByTheLabel()
    {
        (_, string source) = Run(Attributes, RelationModel);

        Assert.Contains("CategoryKey = ScaffoldValues.Text(form.CategoryKey),", source, StringComparison.Ordinal);
        Assert.Contains("<select id=\\\"categoryKey\\\" name=\\\"categoryKey\\\"><option value=\\\"\\\">(none)</option>", source, StringComparison.Ordinal);
        Assert.Contains("Enc(ScaffoldValues.Display(choice.Name))", source, StringComparison.Ordinal);
        Assert.Contains(".ListAsync(null, Array.Empty<string>(), new ScaffoldSortSpec(\"Name\", ScaffoldSortDirection.Ascending), CategoryScaffold.SortFields, null, OptionLimit, ct)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AListedReference_ShowsTheTargetLabelAndFallsBackToTheKey()
    {
        (_, string source) = Run(Attributes, RelationModel);

        Assert.Contains("categoryKeyLabels[ScaffoldValues.Display(choice.Key)] = ScaffoldValues.Display(choice.Name);", source, StringComparison.Ordinal);
        Assert.Contains("Enc(Label(categoryKeyLabels, ScaffoldValues.Display(row.CategoryKey)))", source, StringComparison.Ordinal);
        Assert.Contains("labels.TryGetValue(key, out string? label) ? label : key;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tagSlugsLabels", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnum_RendersASelectOfItsMembersAndRefusesAnythingElse()
    {
        (_, string source) = Run(Attributes, RelationModel);

        Assert.Contains("if (!ScaffoldValues.IsRequiredEnumMember<Demo.Status>(form.Status))", source, StringComparison.Ordinal);
        Assert.Contains("\"Status must be one of Draft, InReview, Published.\"", source, StringComparison.Ordinal);
        Assert.Contains("Status = ScaffoldValues.EnumValue<Demo.Status>(form.Status, default(Demo.Status)),", source, StringComparison.Ordinal);
        Assert.Contains("Mood = ScaffoldValues.NullableEnum<Demo.Status>(form.Mood),", source, StringComparison.Ordinal);
        Assert.Contains("<option value=\\\"InReview\\\"\").Append(statusValue == \"InReview\" ? \" selected\" : \"\").Append(\">In Review</option>", source, StringComparison.Ordinal);
        Assert.Contains("<select id=\\\"mood\\\" name=\\\"mood\\\">\");", source, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(source, "sb.Append(\"<option value=\\\"\\\">(none)</option>\");"));
    }

    [Fact]
    public void ADateOnly_RendersADateInputAndRefusesANonDate()
    {
        (_, string source) = Run(Attributes, RelationModel);

        Assert.Contains("<input type=\\\"date\\\" id=\\\"publishedAt\\\"", source, StringComparison.Ordinal);
        Assert.Contains("if (!ScaffoldValues.IsDateOnly(form.PublishedAt))", source, StringComparison.Ordinal);
        Assert.Contains("\"Published At must be a date.\"", source, StringComparison.Ordinal);
        Assert.Contains("PublishedAt = ScaffoldValues.NullableDateOnly(form.PublishedAt),", source, StringComparison.Ordinal);
        Assert.Contains("Day = ScaffoldValues.DateOnlyValue(form.Day, DateOnly.FromDateTime(DateTime.UtcNow)),", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[ScaffoldReference(typeof(Nowhere))] public string? Key { get; set; }", "NWS003")]
    [InlineData("[ScaffoldReference(typeof(Target))] public string Key { get; set; } = \"\";", "NWS004")]
    [InlineData("[ScaffoldRelation(typeof(Target))] public List<string> Keys { get; set; } = new();", "NWS005")]
    [InlineData("[ScaffoldReference(typeof(Numbered))] public string? Key { get; set; }", "NWS006")]
    public void AMisdeclaredReferenceOrRelation_IsABuildError(string property, string id)
    {
        string model = """
            using System.Collections.Generic;
            using NativeWeb.Scaffold;

            namespace Demo
            {
                public sealed class Nowhere { public string Key { get; set; } = ""; }

                [Scaffold]
                public sealed class Target { [ScaffoldKey] public string Key { get; set; } = ""; }

                [Scaffold]
                public sealed class Numbered { [ScaffoldKey] public int Number { get; set; } }

                [Scaffold]
                public sealed class Owner
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    PROPERTY
                }
            }
            """.Replace("PROPERTY", property, StringComparison.Ordinal);

        (ImmutableArray<Diagnostic> diagnostics, string source) = Run(Attributes, model);

        Assert.Contains(diagnostics, d => d.Id == id);
        Assert.DoesNotContain("class OwnerScaffold", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapOwnerScaffold()", source, StringComparison.Ordinal);
        Assert.Contains("class TargetScaffold", source, StringComparison.Ordinal);
        Assert.Contains("MapTargetScaffold()", source, StringComparison.Ordinal);
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

    // A model whose key, a list column and a plain editable property all live on a
    // base class. Every one of them used to be invisible: ScaffoldGenerator.Build
    // read type.GetMembers(), which returns declared members only.
    //
    // The key is on the base deliberately. It makes the first assertion below a
    // property rather than a spelling -- NWS001 fires when no property carries
    // [ScaffoldKey], so a generator that cannot see the base cannot find the key
    // either, and the diagnostic is the failure rather than a missing string.
    private const string InheritedModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            public abstract class Entity
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList] public string Owner { get; set; } = "";
                public virtual string Note { get; set; } = "";
            }

            [Scaffold]
            public sealed class Memo : Entity
            {
                [ScaffoldList][ScaffoldSearch] public string Title { get; set; } = "";
                public override string Note { get; set; } = "";
            }
        }
        """;

    [Fact]
    public void AnInheritedKey_IsFound()
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Attributes, InheritedModel);

        Assert.Empty(diagnostics);
    }

    // The defect this is written for lost data rather than failing: Build emits an
    // object initializer over exactly the properties the model knows about, so an
    // inherited property got no assignment and every save through the scaffold
    // wrote its default over whatever the row held. Nothing reported it.
    [Fact]
    public void AnInheritedProperty_IsAssignedFromTheFormLikeADeclaredOne()
    {
        (_, string emitted) = Run(Attributes, InheritedModel);

        Assert.Contains("Owner = ScaffoldValues.Text(form.Owner) ?? string.Empty,", emitted, StringComparison.Ordinal);
        Assert.Contains("Title = ScaffoldValues.Text(form.Title) ?? string.Empty,", emitted, StringComparison.Ordinal);
        Assert.Contains("Id = existing is null ? default! : existing.Id,", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInheritedListColumn_IsRendered()
    {
        (_, string emitted) = Run(Attributes, InheritedModel);

        Assert.Contains("ScaffoldLayout.Header(\"Owner\"", emitted, StringComparison.Ordinal);
        Assert.Contains("row.Owner", emitted, StringComparison.Ordinal);
    }

    // An override is one property to the C# reader of Memo, and two symbols to
    // Roslyn. Emitting both puts the same name twice in one object initializer,
    // which is CS1912 in the consumer's build.
    [Fact]
    public void AnOverriddenProperty_IsEmittedOnce()
    {
        (_, string emitted) = Run(Attributes, InheritedModel);

        Assert.Equal(1, Occurrences(emitted, "Note = ScaffoldValues.Text(form.Note) ?? string.Empty,"));
        Assert.Equal(1, Occurrences(emitted, "string? Note"));
    }

    [Fact]
    public void AModelWithAnInheritedKeyAndProperties_EmitsCodeThatCompiles() =>
        AssertGeneratedSourceCompiles(RealAttributeInheritedModel);

    // The same shape as InheritedModel against the real attributes, for the compile
    // gate: two definitions of one attribute name would be ambiguous rather than
    // convenient, so it cannot share the inline copies.
    private const string RealAttributeInheritedModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            public abstract class Entity
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList] public string Owner { get; set; } = "";
                public virtual string Note { get; set; } = "";
                public System.DateTime CreatedAt { get; set; }
            }

            [Scaffold]
            public sealed class Memo : Entity
            {
                [ScaffoldList][ScaffoldSearch] public string Title { get; set; } = "";
                public override string Note { get; set; } = "";
            }
        }
        """;

    [Fact]
    public void ASecondScaffoldKey_ReportsNws007()
    {
        const string twoKeys = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Widget
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    [ScaffoldKey] public string Sku { get; set; } = "";
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, twoKeys);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWS007", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // Both names, because which one silently won is the thing the author has to
        // see to fix it. Sku won before this: the last [ScaffoldKey] overwrote the
        // first, and the routes were keyed by it.
        string message = diagnostic.GetMessage();
        Assert.Contains("Id", message, StringComparison.Ordinal);
        Assert.Contains("Sku", message, StringComparison.Ordinal);

        Assert.DoesNotContain("WidgetScaffold", emitted, StringComparison.Ordinal);
    }

    // Two [Scaffold] types with one short name collide twice: on the generator hint
    // name, which throws inside the driver, and on the emitted class name, because
    // every model is emitted into NativeWeb.Scaffold.Generated as TypeName +
    // "Scaffold". Renaming the hint alone would move the crash to CS0101.
    [Fact]
    public void TwoModelsSharingAShortName_ReportNws008()
    {
        const string colliding = """
            using NativeWeb.Scaffold;

            namespace Demo.Blog
            {
                [Scaffold]
                public sealed class Article
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                }
            }

            namespace Demo.News
            {
                [Scaffold]
                public sealed class Article
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, colliding);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWS008", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        string message = diagnostic.GetMessage();
        Assert.Contains("Demo.Blog.Article", message, StringComparison.Ordinal);
        Assert.Contains("Demo.News.Article", message, StringComparison.Ordinal);

        // Neither is emitted. Emitting the first and refusing the second would leave
        // a build that half works and a diagnostic naming the type that did.
        Assert.DoesNotContain("ArticleScaffold", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoModelsWithDistinctShortNames_ShareANamespaceWithoutComplaint()
    {
        const string distinct = """
            using NativeWeb.Scaffold;

            namespace Demo.Blog
            {
                [Scaffold]
                public sealed class Article
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                }
            }

            namespace Demo.News
            {
                [Scaffold]
                public sealed class Bulletin
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, distinct);

        Assert.Empty(diagnostics);
        Assert.Contains("ArticleScaffold", emitted, StringComparison.Ordinal);
        Assert.Contains("BulletinScaffold", emitted, StringComparison.Ordinal);
    }

    // The cross-model half of NWS007. A target carrying two keys is refused and
    // never emitted, so a model referring to it would otherwise emit a control
    // reading TargetScaffold.SortFields against a class that does not exist -- a
    // CS0103 cascade beside the one diagnostic that names the real fault, which is
    // the failure mode ScaffoldGenerator.Emit already refuses models to avoid.
    [Fact]
    public void AReferenceToATargetWithTwoKeys_IsRefusedAlongWithTheTarget()
    {
        const string source = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Target
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    [ScaffoldKey] public string Sku { get; set; } = "";
                }

                [Scaffold]
                public sealed class Holder
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    [ScaffoldReference(typeof(Target))] public string? TargetId { get; set; }
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, source);

        Assert.Equal(
            ["NWS003", "NWS007"],
            diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("TargetScaffold", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("HolderScaffold", emitted, StringComparison.Ordinal);
    }
}
