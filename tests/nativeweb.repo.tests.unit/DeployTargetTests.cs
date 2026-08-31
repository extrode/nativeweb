using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The deploy chain names a project that exists, and one binary name end to end.
/// </summary>
/// <remarks>
/// <para>
/// Both facts are about text in shell and unit files that no suite executes: the
/// deploy scripts have never run on a real host (spec 011 T15), so a stale target
/// in them is invisible until an operator is halfway through a release. That is
/// how <c>build.sh</c> came to publish <c>apps/web/nativeweb.web.csproj</c> for
/// the whole of the day after <c>apps/web</c> left the repository.
/// </para>
/// <para>
/// The binary name is the sharper of the two. systemd will not expand a variable
/// in the first token of <c>ExecStart</c>, so <c>nativeweb@.service</c> names the
/// executable literally; if <c>build.sh</c> installs it under any other name the
/// unit's <c>ConditionPathExists</c> is false and systemd skips the unit
/// <em>silently</em> — a colour that never starts and never says why.
/// </para>
/// </remarks>
public sealed class DeployTargetTests
{
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

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. segments]));

    private const string Binary = "nativeweb";

    [Fact]
    public void BuildScript_PublishesAProjectThatExists()
    {
        string build = Read("scripts", "deploy", "build.sh");

        const string marker = "PROJECT=\"${PROJECT:-";
        int start = build.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "build.sh no longer defaults PROJECT; this test is looking for the wrong line.");

        start += marker.Length;
        string project = build[start..build.IndexOf('}', start)];

        Assert.True(
            File.Exists(Path.Combine(RepoRoot(), project.Replace('/', Path.DirectorySeparatorChar))),
            $"scripts/deploy/build.sh publishes '{project}', which is not in this repository. "
                + "A deploy from this branch fails on the missing project file, on the host, "
                + "after the operator has already provisioned it.");
    }

    [Fact]
    public void TheDeployChain_AgreesOnOneBinaryName()
    {
        Assert.Contains($"APP_BINARY=\"{Binary}\"", Read("scripts", "deploy", "build.sh"), StringComparison.Ordinal);
        Assert.Contains($"chmod 0755 \"$RELEASE_DIR/{Binary}\"", Read("scripts", "deploy", "deploy.sh"), StringComparison.Ordinal);

        string unit = Read("scripts", "deploy", "systemd", "nativeweb@.service");
        Assert.Contains($"ExecStart=/opt/nativeweb/%i/{Binary}\n", unit, StringComparison.Ordinal);
        Assert.Contains($"ConditionPathExists=/opt/nativeweb/%i/{Binary}\n", unit, StringComparison.Ordinal);
    }

    // The project name must not reappear anywhere downstream of the publish: the
    // point of the rename in build.sh is that one product-specific string exists,
    // in one place, overridable by env.
    [Fact]
    public void NothingDownstreamOfTheBuild_NamesAProject()
    {
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "scripts", "deploy"),
            "*",
            SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "build.sh")
            {
                continue;
            }

            string text = File.ReadAllText(file);

            Assert.DoesNotContain("apps/web", text, StringComparison.Ordinal);
            Assert.DoesNotContain("apps/sample", text, StringComparison.Ordinal);
        }
    }
}
