using System.Text.RegularExpressions;
using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The deploy chain names a project that exists, and one binary name end to end.
/// </summary>
/// <remarks>
/// <para>
/// Both facts are about text in shell and unit files that no suite executes, so a
/// stale target in them is invisible until an operator is halfway through a
/// release. That is how <c>build.sh</c> came to publish
/// <c>apps/web/nativeweb.web.csproj</c> for the whole of the day after
/// <c>apps/web</c> left the repository. The chain first ran against a real host on
/// 2026-09-01 (spec 011 T15).
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

    // Presence is not placement, and the assertion above only ever checked presence.
    // Condition*, Assert* and StartLimit* are [Unit] options: in [Service] systemd
    // logs "Unknown key name" and ignores the line. ConditionPathExists sat in
    // [Service] from the file's creation until 2026-09-01, when the first real
    // deploy printed that warning six times and started a colour whose release
    // directory the condition was supposed to have checked.
    [Fact]
    public void UnitOnlyDirectives_AreInTheUnitSection()
    {
        string unit = Read("scripts", "deploy", "systemd", "nativeweb@.service");
        string section = "(none)";
        List<string> misplaced = [];

        foreach (string raw in unit.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line;
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            string key = line[..line.IndexOf('=', StringComparison.Ordinal)];
            bool unitOnly = key.StartsWith("Condition", StringComparison.Ordinal)
                || key.StartsWith("Assert", StringComparison.Ordinal)
                || key.StartsWith("StartLimit", StringComparison.Ordinal);

            if (unitOnly && section != "[Unit]")
            {
                misplaced.Add($"{key} in {section}");
            }
        }

        Assert.True(
            misplaced.Count == 0,
            $"nativeweb@.service puts {string.Join(", ", misplaced)}. systemd reads these only in "
                + "[Unit]; elsewhere it warns and ignores them, so the directive does nothing on "
                + "every host that has ever run this unit.");
    }

    // Four files have to agree on one path for /docs to serve anything: build.sh
    // fixes the destination inside the artifact, deploy.sh links it, rollback.sh
    // links it back, and the Caddyfile roots at the link. Three of the four spell
    // it as a literal, so nothing but this test notices when one of them moves.
    [Fact]
    public void TheDocsSite_TravelsToOnePathTheWholeChainNames()
    {
        string build = Read("scripts", "deploy", "build.sh");
        string deploy = Read("scripts", "deploy", "deploy.sh");
        string rollback = Read("scripts", "deploy", "rollback.sh");
        string caddy = Read("scripts", "deploy", "caddy", "Caddyfile");

        // build.sh assigns ARTIFACT_DOCS_DIR twice: empty at the top, because
        // absent docs are not an error, and then the real destination inside the
        // branch that found a site. The first is the declaration, not the value.
        const string marker = "ARTIFACT_DOCS_DIR=\"";
        List<string> assigned = [];
        for (int at = build.IndexOf(marker, StringComparison.Ordinal); at >= 0;
            at = build.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            int from = at + marker.Length;
            assigned.Add(build[from..build.IndexOf('"', from)]);
        }

        Assert.True(assigned.Count >= 1, "build.sh no longer fixes the artifact's docs destination.");

        string inArtifact = assigned.Find(v => v.Length > 0)
            ?? throw new InvalidOperationException(
                "build.sh assigns ARTIFACT_DOCS_DIR only the empty string, so no artifact ever carries docs.");

        Assert.Contains($"cp -r \"$DOCS_DIR\" \"$PUBLISH_DIR/$ARTIFACT_DOCS_DIR\"", build, StringComparison.Ordinal);
        Assert.Contains("docs_dir=$ARTIFACT_DOCS_DIR", build, StringComparison.Ordinal);

        Assert.Contains("sed -n 's/^docs_dir=//p' \"$STAGE/RELEASE\"", deploy, StringComparison.Ordinal);

        const string link = "/opt/nativeweb/docs";
        Assert.Contains($"ln -sfn \"/opt/nativeweb/$IDLE/$DOCS_DIR\" {link}", deploy, StringComparison.Ordinal);
        Assert.Contains($"ln -sfn \"/opt/nativeweb/$PREVIOUS_COLOUR/{inArtifact}\" {link}", rollback, StringComparison.Ordinal);
        Assert.Contains($"root * {link}", caddy, StringComparison.Ordinal);

        // Without the redirect, /docs is served the index directly -- handle_path
        // strips the prefix, so file_server sees "/" and has no directory path to
        // redirect. The index's links are relative and then resolve one level too
        // high. Measured: /docs 200, the link it carried 404.
        Assert.Contains("redir /docs /docs/ 308", caddy, StringComparison.Ordinal);
        Assert.Contains("handle_path /docs/* {", caddy, StringComparison.Ordinal);
    }

    // The release root is chmod'd o=, and Caddy is not in the nativeweb group, so
    // without a widening step /docs is a 403 that reads as a web-server bug. The
    // traversal bit on the release root has to come with it: a world-readable
    // subtree under a 0750 parent is unreachable.
    [Fact]
    public void TheDocsSubtree_IsReadableByTheProcessThatServesIt()
    {
        string deploy = Read("scripts", "deploy", "deploy.sh");

        int restrict = deploy.IndexOf("chmod -R u=rwX,g=rX,o= \"$RELEASE_DIR\"", StringComparison.Ordinal);
        Assert.True(restrict >= 0, "deploy.sh no longer restricts the release directory; this test reads the wrong mechanism.");

        int traverse = deploy.IndexOf("chmod o=x \"$RELEASE_DIR\"", StringComparison.Ordinal);
        int widen = deploy.IndexOf("chmod -R a=rX,u=rwX \"$RELEASE_DIR/$DOCS_DIR\"", StringComparison.Ordinal);

        Assert.True(traverse > restrict, "deploy.sh does not grant traversal on the release root after restricting it, so the docs are unreachable through a 0750 parent.");
        Assert.True(widen > restrict, "deploy.sh does not widen the docs subtree after restricting the release root, so Caddy gets a 403 on every page.");
    }

    // cp -a copies the source directory's own mode onto the destination, and the
    // stage is mktemp -d, which is 0700. A subtractive chmod cannot add back a
    // group bit that was never granted, so the release directory stayed
    // drwx------ and systemd failed the unit at 200/CHDIR on 2026-09-01.
    [Fact]
    public void TheDeployScript_SetsTheReleaseDirectoryModeAbsolutely()
    {
        string deploy = Read("scripts", "deploy", "deploy.sh");

        List<string> subtractive = [];
        bool absolute = false;

        foreach (string raw in deploy.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('#') || !line.StartsWith("chmod ", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.EndsWith("\"$RELEASE_DIR\"", StringComparison.Ordinal))
            {
                continue;
            }

            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string mode = words[^2];

            if (mode.Contains('='))
            {
                absolute = true;
            }
            else
            {
                subtractive.Add(mode);
            }
        }

        Assert.True(
            absolute,
            "deploy.sh no longer sets an absolute mode on $RELEASE_DIR. cp -a stamps the 0700 of "
                + "mktemp -d onto it, so without one the service account cannot chdir into its own "
                + "release and systemd fails the unit at 200/CHDIR.");

        Assert.True(
            subtractive.Count == 0,
            $"deploy.sh applies {string.Join(", ", subtractive)} to $RELEASE_DIR. A subtractive mode "
                + "cannot restore the group bits mktemp -d never granted.");
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

        // And the capture is read where the stamp is: pinning the spelling alone
        // stayed green with the whole `if [[ -z ... ]]` block deleted, which is the
        // regression this test is named for.
        const string branch = "if [[ -z \"$MIGRATIONS_DIR_SET\" ]]; then";
        int start = deploy.IndexOf(branch, StringComparison.Ordinal);
        Assert.True(start >= 0, "deploy.sh no longer reads MIGRATIONS_DIR_SET, so nothing decides whether to trust the stamp.");

        int end = deploy.IndexOf("\nfi", start, StringComparison.Ordinal);
        Assert.True(end > start, "the MIGRATIONS_DIR_SET branch has no closing fi.");

        Assert.Contains("sed -n 's/^migrations_dir=//p' \"$STAGE/RELEASE\"", deploy[start..end], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_RestoresTheModeItPublishes()
    {
        string build = Read("scripts", "deploy", "build.sh");

        string restore = Invocation(build, "dotnet restore \"$PROJECT\"");
        string publish = Invocation(build, "dotnet publish \"$PROJECT\"");

        Assert.Equal(JauntyQMode(publish, "publish"), JauntyQMode(restore, "restore"));

        // The RID and configuration decide which assets restore asks the feed for, so a
        // preflight without them can pass where the publish then fails on the very error
        // the preflight exists to intercept.
        foreach (string flag in new[] { "-c Release", "-r \"$RUNTIME_ID\"" })
        {
            Assert.Contains(flag, restore, StringComparison.Ordinal);
            Assert.Contains(flag, publish, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildScript_TellsTheOperatorToCredentialTheSourceJauntyQResolvesFrom()
    {
        string build = Read("scripts", "deploy", "build.sh");

        var mapped = XDocument.Parse(Read("nuget.config"))
            .Descendants("packageSource")
            .Where(e => e.Elements("package")
                .Any(pkg => (string?)pkg.Attribute("pattern") == "JauntyQ.*"))
            .Select(e => (string?)e.Attribute("key"))
            .ToList();

        Assert.True(mapped.Count == 1, $"nuget.config maps {mapped.Count} sources for JauntyQ.*; build.sh names one.");
        string source = mapped[0]!;

        Assert.Contains($"JAUNTYQ_FEED_SOURCE=\"{source}\"", build, StringComparison.Ordinal);

        // Inside the branch that prints it, not merely somewhere in the file: the remedy
        // text survives in a comment when the branch itself is deleted, and a fact that
        // stays green through that tests nothing an operator ever sees.
        const string guard = "if grep -qiE '401";
        int start = build.IndexOf(guard, StringComparison.Ordinal);
        Assert.True(start >= 0, "build.sh no longer classifies the restore failure, so no remedy is reachable.");

        int end = build.IndexOf("\n\tfi\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "the restore-failure branch has no closing fi.");

        Assert.Contains("dotnet nuget update source $JAUNTYQ_FEED_SOURCE", build[start..end], StringComparison.Ordinal);
        Assert.Contains("read:packages", build[start..end], StringComparison.Ordinal);
    }

    // A single invocation, from its command up to the last line of a backslash
    // continuation. Scoping every assertion below to one of these is what stops a flag
    // mentioned in a nearby comment from standing in for the flag actually passed.
    private static string Invocation(string script, string command)
    {
        int start = script.IndexOf(command, StringComparison.Ordinal);
        Assert.True(start >= 0, $"scripts/deploy/build.sh does not run `{command}`.");

        int end = start;
        while (true)
        {
            int newline = script.IndexOf('\n', end);
            Assert.True(newline > 0, $"`{command}` runs to the end of the file.");

            if (!script[end..newline].TrimEnd().EndsWith('\\'))
            {
                return script[start..newline];
            }

            end = newline + 1;
        }
    }

    private static string JauntyQMode(string invocation, string what)
    {
        Match match = Regex.Match(invocation, @"-p:JauntyQUsePackages=([A-Za-z]+)");
        Assert.True(match.Success, $"the {what} in build.sh does not state a JauntyQ resolution mode.");
        return match.Groups[1].Value;
    }

    private static string Defaulted(string script, string marker)
    {
        int start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"scripts/deploy/build.sh has no line matching `{marker}`.");

        start += marker.Length;
        return script[start..script.IndexOf('}', start)];
    }
}
