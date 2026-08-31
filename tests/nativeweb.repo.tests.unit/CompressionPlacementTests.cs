using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Compression happens at Caddy and not in the app, and both halves of that are
/// asserted here because either half alone is a silent regression.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-31 against <c>apps/sample</c>, three 10s runs at concurrency
/// 16, published NativeAOT Release. Through the proxy every product is deployed
/// behind, Caddy's <c>encode</c> served <c>/articles</c> at 9,002 req/s where the
/// app compressing it managed 6,255 — and compressing in-process also strips the
/// <c>Content-Length</c> the renderer declares, and re-runs on every output-cache
/// hit because that step sits above <c>UseOutputCache</c>. The four-variant table
/// is bottleneck 4 in <c>docs/02-reference/performance.md</c>.
/// </para>
/// <para>
/// A file-level assertion rather than a behavioural one, because the two facts
/// live in two artifacts nothing else compares: a Caddyfile this repository ships
/// but never runs in a test, and a middleware registration whose absence is
/// invisible from any response. Losing the <c>encode</c> line would ship every
/// page uncompressed to real clients with nothing failing.
/// </para>
/// </remarks>
public sealed class CompressionPlacementTests
{
    [Theory]
    [InlineData("Caddyfile")]
    [InlineData("Caddyfile.edge")]
    public void EveryDeployedCaddyCompresses(string name)
    {
        string path = Path.Combine(RepoRoot(), "scripts", "deploy", "caddy", name);
        Assert.True(File.Exists(path), $"Missing: {path}");

        string config = File.ReadAllText(path);

        // zstd first because a current browser prefers it. Brotli is absent
        // because stock Caddy has no encoder for it: adding br to that directive
        // fails config validation with "module not registered: http.encoders.br",
        // so a well-meant addition would break the deploy rather than improve the
        // ratio. The second assertion is what stops it being added back.
        Assert.Contains("encode zstd gzip", config, StringComparison.Ordinal);
        Assert.DoesNotContain("encode zstd br gzip", config, StringComparison.Ordinal);
    }

    [Fact]
    public void TheComposedPipelineDoesNotCompress()
    {
        string source = File.ReadAllText(
            Path.Combine(RepoRoot(), "libs", "nativeweb.web", "NativeWebPipeline.cs"));

        int body = source.IndexOf("public static WebApplication UseNativeWebPipeline", StringComparison.Ordinal);
        Assert.True(body > 0, "UseNativeWebPipeline not found.");

        // Only the call inside the composed pipeline is forbidden. The
        // registration above it stays, and stays public: a host with no
        // compressing proxy in front of it adds one line and gets the same
        // configuration back.
        Assert.DoesNotContain("app.UseResponseCompression()", source[body..], StringComparison.Ordinal);
        Assert.Contains("AddNativeWebResponseCompression", source, StringComparison.Ordinal);
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
