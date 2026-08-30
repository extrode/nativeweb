using Xunit;

namespace NativeWeb.Web.Tests.Unit;

// Registration order in Program.cs IS the pipeline nesting order, and nothing in
// the integration tier can see it: a request that never throws inside the rewrite
// or the route matcher takes the same path either way, which is why the 2026-08-19
// audit found ErrorPageMiddleware and SecurityHeadersMiddleware sitting BELOW both
// of them with 300-odd green tests above the finding.
//
// A source-order assertion rather than a behavioural one, and named so. The failure
// this guards is a throw from the clean-URL rewrite or from route matching escaping
// to Kestrel, which answers with a bare 500 carrying no error shell and no security
// headers. Making that throw happen on demand would mean injecting a fault into a
// middleware whose whole body is a dictionary lookup -- so what is asserted is the
// position that makes it impossible.
public sealed class PipelineOrderTests
{
    private static readonly string Program = File.ReadAllText(
        Path.Combine(RepoRoot(), "apps", "web", "Program.cs"));

    [Theory]
    // The rewrite runs before route matching (spec 003 R38) and both run inside the
    // error shell, so the four have exactly one valid order.
    [InlineData("app.UseMiddleware<ErrorPageMiddleware>", "app.UseMiddleware<SecurityHeadersMiddleware>")]
    [InlineData("app.UseMiddleware<SecurityHeadersMiddleware>", "app.UseMiddleware<StaticPagesMiddleware>")]
    [InlineData("app.UseMiddleware<StaticPagesMiddleware>", "app.UseRouting()")]
    public void The_error_shell_wraps_the_rewrite_and_the_router(string outer, string inner)
    {
        int outerAt = Program.IndexOf(outer, StringComparison.Ordinal);
        int innerAt = Program.IndexOf(inner, StringComparison.Ordinal);

        Assert.True(outerAt >= 0, $"'{outer}' is not registered in Program.cs at all.");
        Assert.True(innerAt >= 0, $"'{inner}' is not registered in Program.cs at all.");
        Assert.True(
            outerAt < innerAt,
            $"'{outer}' must be registered before '{inner}': first registered is outermost, and "
            + "everything the error shell and the security headers are meant to cover has to sit inside them.");
    }

    // UseForwardedHeaders stays outside the error shell on purpose -- every
    // middleware below it, the error path included, reads the client address and
    // scheme it corrects. Asserted so a future reshuffle that "tidies" the four
    // above cannot quietly drag this one down with them.
    [Fact]
    public void Forwarded_headers_stay_outermost()
    {
        int forwardedAt = Program.IndexOf("app.UseForwardedHeaders()", StringComparison.Ordinal);
        int errorAt = Program.IndexOf("app.UseMiddleware<ErrorPageMiddleware>", StringComparison.Ordinal);

        Assert.True(forwardedAt >= 0, "app.UseForwardedHeaders() is not registered in Program.cs at all.");
        Assert.True(forwardedAt < errorAt);
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
