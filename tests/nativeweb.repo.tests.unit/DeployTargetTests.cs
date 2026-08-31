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
        // Read out of build.sh rather than written here. The two literals below are
        // historical: this test never learned what the project name IS, so
        // retargeting build.sh at a third project would have left that third name
        // free to leak downstream with the assertion still green.
        string current = Path.GetDirectoryName(
            Defaulted(Read("scripts", "deploy", "build.sh"), "PROJECT=\"${PROJECT:-").Replace('\\', '/'))!
            .Replace('\\', '/');

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
            Assert.DoesNotContain(current, text, StringComparison.Ordinal);

            Assert.DoesNotContain("NativeWeb__", text, StringComparison.Ordinal);
        }
    }

    // The configuration prefix is the class of coupling the test above misses: a
    // key name is not a project name, so ten NativeWeb__* keys sat in deploy.sh
    // and provision.sh while apps/sample bound Sample:. The preflight hard-failed
    // on nine keys nothing reads, and passed on the two that mattered while they
    // stayed unset. Derived from the parsed PROJECT rather than hardcoded, so
    // retargeting the default project moves this assertion with it.
    [Fact]
    public void BuildScript_StampsThePrefixTheDefaultProjectReads()
    {
        string build = Read("scripts", "deploy", "build.sh");

        string project = Defaulted(build, "PROJECT=\"${PROJECT:-");
        string stamped = Defaulted(build, "CONFIG_PREFIX=\"${CONFIG_PREFIX:-");

        string programPath = Path.Combine(
            RepoRoot(),
            Path.GetDirectoryName(project.Replace('/', Path.DirectorySeparatorChar))!,
            "Program.cs");

        Assert.True(File.Exists(programPath), $"build.sh publishes {project}, which has no Program.cs beside it.");

        const string call = "NativeWebOptions.FromConfiguration(builder.Configuration, \"";
        string program = File.ReadAllText(programPath);
        int start = program.IndexOf(call, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{programPath} does not call NativeWebOptions.FromConfiguration with a literal section.");

        start += call.Length;
        string bound = program[start..program.IndexOf('"', start)];

        Assert.True(
            string.Equals(stamped, bound, StringComparison.Ordinal),
            $"build.sh stamps config_prefix={stamped} but the binary it publishes binds '{bound}'. "
                + "deploy.sh preflights the stamped prefix, so it would demand keys this binary never reads "
                + "and pass while the ones it does read stayed unset.");
    }

    [Fact]
    public void TheDeployScript_TakesTheMigrationsDirectoryFromTheArtifact()
    {
        string deploy = Read("scripts", "deploy", "deploy.sh");

        Assert.Contains("sed -n 's/^config_prefix=//p' \"$STAGE/RELEASE\"", deploy, StringComparison.Ordinal);
        Assert.Contains("sed -n 's/^migrations_dir=//p' \"$STAGE/RELEASE\"", deploy, StringComparison.Ordinal);

        string build = Read("scripts", "deploy", "build.sh");
        Assert.Contains("config_prefix=$CONFIG_PREFIX", build, StringComparison.Ordinal);

        // The stamped path is the one inside the artifact, not the one the operator
        // gave. Only $DB_DIR travels, and it arrives as db/, so stamping the
        // operator's MIGRATIONS_DIR named a directory no artifact contains whenever
        // DB_DIR was anything but "db" -- and deploy.sh resolves the stamp
        // release-relative, so it died on the host after the whole publish.
        Assert.Contains("migrations_dir=$ARTIFACT_MIGRATIONS_DIR", build, StringComparison.Ordinal);
        Assert.Contains("ARTIFACT_MIGRATIONS_DIR=\"db/${MIGRATIONS_DIR#\"$DB_DIR\"/}\"", build, StringComparison.Ordinal);
    }

    // Both remote transports forward MIGRATIONS_DIR unconditionally, and this
    // script re-executes itself on the far side. So the host half always sees the
    // variable SET -- to the empty string when nothing was overridden -- and a `+`
    // test counted that as an override: the RELEASE stamp went unread, migrate.sh
    // was handed the release root, and every --host and --incus deploy died on its
    // compgen guard. Only --local ever reached the stamp.
    [Fact]
    public void TheDeployScript_TreatsAnEmptyForwardedMigrationsDirAsUnset()
    {
        string deploy = Read("scripts", "deploy", "deploy.sh");

        Assert.True(
            deploy.Contains("--env MIGRATIONS_DIR=\"$MIGRATIONS_DIR\"", StringComparison.Ordinal)
                || deploy.Contains("MIGRATIONS_DIR=$(printf %q \"$MIGRATIONS_DIR\")", StringComparison.Ordinal),
            "deploy.sh no longer forwards MIGRATIONS_DIR to the host half; this test is reading the wrong mechanism.");

        Assert.Contains("MIGRATIONS_DIR_SET=\"${MIGRATIONS_DIR:+yes}\"", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("MIGRATIONS_DIR_SET=\"${MIGRATIONS_DIR+yes}\"", deploy, StringComparison.Ordinal);
    }

    private static string Defaulted(string script, string marker)
    {
        int start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"scripts/deploy/build.sh has no line matching `{marker}`.");

        start += marker.Length;
        return script[start..script.IndexOf('}', start)];
    }
}
