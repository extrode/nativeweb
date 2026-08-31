using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The skeleton's <c>Program.cs</c> stays small (spec 015 R8).
/// </summary>
/// <remarks>
/// <para>
/// It was 487 lines, and every one of them was framework wiring copied into each
/// product at stamp time: Kestrel limits, forwarded headers, telemetry, data
/// protection, antiforgery, rate limiting, the output cache, the middleware
/// order, <c>/healthz</c>, <c>/version</c>, <c>/readyz</c>, <c>/csp-report</c>,
/// <c>security.txt</c>. A security fix to any of it could not reach a product
/// already stamped, which is the defect spec 015 exists to remove. The ceiling is
/// what stops it growing back one convenient line at a time.
/// </para>
/// <para>
/// Code lines, with comments and blanks excluded. This repository's house style
/// is heavy explanatory comment, and a physical-line ceiling would either punish
/// the file for explaining itself or be set so high it measured nothing. What the
/// spec is about is how much a product has to carry, and a comment is not carried
/// — it is read once and then it is a comment.
/// </para>
/// </remarks>
public sealed class SkeletonEntryPointTests
{
    private const int Ceiling = 60;

    [Fact]
    public void TheStampedEntryPointStaysUnderTheCeiling()
    {
        string path = Path.Combine(RepoRoot(), "skeleton", "apps", "__product__", "Program.cs");
        Assert.True(File.Exists(path), $"Missing: {path}");

        string[] code = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length != 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            code.Length <= Ceiling,
            $"skeleton/apps/__product__/Program.cs is {code.Length} lines of code, over the {Ceiling} "
                + "the spec allows. Anything that is not this product's own configuration section, "
                + "routes or templates belongs in libs/nativeweb.web, where a fix reaches a product "
                + "that has already been stamped.");
    }

    [Fact]
    public void TheEntryPointStillDoesTheThingsTheCeilingIsAbout()
    {
        // The control. A file emptied to satisfy the ceiling would pass the fact
        // above and stamp a product that serves nothing.
        string source = File.ReadAllText(
            Path.Combine(RepoRoot(), "skeleton", "apps", "__product__", "Program.cs"));

        Assert.Contains("AddNativeWebWeb(options, typeof(Program).Assembly)", source, StringComparison.Ordinal);
        Assert.Contains("UseNativeWebPipeline(Locales.All, auth =>", source, StringComparison.Ordinal);
        Assert.Contains("auth.UseAuthentication()", source, StringComparison.Ordinal);
        Assert.Contains("auth.UseAuthorization()", source, StringComparison.Ordinal);
        Assert.Contains("MapNativeWebEndpoints(options, typeof(Program).Assembly)", source, StringComparison.Ordinal);
        Assert.Contains("app.MapStaticAssets()", source, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/\"", source, StringComparison.Ordinal);
        Assert.Contains("app.Run()", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NativeWeb.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }
}
