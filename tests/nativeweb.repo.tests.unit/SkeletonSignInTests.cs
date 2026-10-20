using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// A stamped product carries the sign-in library and registers the scheme (spec 016 R11, R28).
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this repository compiles the skeleton — it is a template stamped by
/// <c>tools/new-product.ps1</c> — so what a stamped product does at runtime is proved where
/// the code actually runs: <c>tests/nativeweb.signin.tests.integration</c> shows a protected
/// route redirecting an anonymous request to the login path, and
/// <c>tests/nativeweb.sample.tests.integration</c> shows the same in an application. These
/// facts cover the part those cannot: that the template a product is stamped from carries
/// the wiring at all, in both resolution modes.
/// </para>
/// <para>
/// The reference in one mode and not the other is the failure worth catching: it builds on a
/// developer machine with a framework checkout beside it and fails on the runner, or the
/// reverse, and either way the error arrives after the stamp rather than before it.
/// </para>
/// </remarks>
public sealed class SkeletonSignInTests
{
    [Fact]
    public void CheckoutMode_ReferencesTheLibrary()
    {
        XDocument project = Skeleton("apps/__product__/__product__.csproj");

        string[] references = project.Descendants("ItemGroup")
            .Where(group => Condition(group).Contains("'$(NativeWebUsePackages)' != 'true'", StringComparison.Ordinal))
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .ToArray();

        Assert.Contains(references, r => r.EndsWith("nativeweb.signin.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageMode_ReferencesTheLibrary()
    {
        XDocument project = Skeleton("apps/__product__/__product__.csproj");

        string[] references = project.Descendants("ItemGroup")
            .Where(group => Condition(group).Contains("'$(NativeWebUsePackages)' == 'true'", StringComparison.Ordinal))
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .ToArray();

        Assert.Contains("Extrode.NativeWeb.SignIn", references);
    }

    [Fact]
    public void PackageMode_PinsTheLibrarysVersion()
    {
        // A PackageReference with no PackageVersion beside it fails restore under central
        // package management, and it fails only in the mode nobody builds locally.
        XDocument packages = Skeleton("Directory.Packages.props");

        Assert.Contains(
            packages.Descendants("PackageVersion"),
            version => version.Attribute("Include")?.Value == "Extrode.NativeWeb.SignIn"
                && !string.IsNullOrWhiteSpace(version.Attribute("Version")?.Value));
    }

    [Fact]
    public void TheEntryPoint_RegistersTheSchemeFromConfiguration()
    {
        string source = SkeletonText("apps/__product__/Program.cs");

        Assert.Contains("builder.Services.AddSignIn(signIn)", source, StringComparison.Ordinal);
        Assert.Contains("app.MapSignIn()", source, StringComparison.Ordinal);
        Assert.Contains("__PRODUCT__:SignIn", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEntryPoint_RegistersNothingWhenNoProviderIsConfigured()
    {
        // The guard around each call, not the guard anywhere in the file. A skeleton that
        // registered unconditionally would fail startup validation in every product stamped
        // before it had a provider, which is every product on the day it is stamped -- and
        // the first version of this fact passed with the registration's guard deleted,
        // because the sign-out endpoint's guard below still matched.
        // Newlines normalised: the file is edited on two platforms and the guard would
        // otherwise match or not by line ending.
        string source = SkeletonText("apps/__product__/Program.cs").ReplaceLineEndings("\n");
        const string guard = "if (signIn[\"AuthorizationEndpoint\"] is { Length: > 0 })";

        Assert.Contains(Guarded(guard, "builder.Services.AddSignIn(signIn);"), source, StringComparison.Ordinal);
        Assert.Contains(Guarded(guard, "app.MapSignIn();"), source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStampedSettings_CarryTheSectionTheEntryPointReads()
    {
        string settings = SkeletonText("apps/__product__/appsettings.json");

        Assert.Contains("\"SignIn\": {", settings, StringComparison.Ordinal);

        foreach (string key in new[]
        {
            "AuthorizationEndpoint", "TokenEndpoint", "UserInfoEndpoint", "IntrospectionEndpoint",
            "ClientId", "ClientSecret", "RedirectUri", "StoreEncryptionKey",
        })
        {
            Assert.Contains($"\"{key}\": \"\"", settings, StringComparison.Ordinal);
        }
    }

    private static string Guarded(string guard, string call) =>
        $"        {guard}\n        {{\n            {call}\n        }}";

    private static string Condition(XElement group) => group.Attribute("Condition")?.Value ?? "";

    private static XDocument Skeleton(string relative) => XDocument.Parse(SkeletonText(relative));

    private static string SkeletonText(string relative)
    {
        string path = Path.Combine(RepoRoot(), "skeleton", relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing: {path}");

        return File.ReadAllText(path);
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
