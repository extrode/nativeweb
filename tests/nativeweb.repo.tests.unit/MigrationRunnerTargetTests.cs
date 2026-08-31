using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The migration runner points at a schema that is in this repository, and does
/// not require one that is not.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in any suite executes <c>db/migrate.sh</c>: it needs a live PostgreSQL
/// and a role that can authenticate, and the one thing that does drive it —
/// <c>db/test-run-lock.sh</c> — is a shell script run by hand for the reasons its
/// own header gives. So a stale path in it is caught by an operator mid-deploy or
/// by nobody, which is what happened when <c>db/migrations/</c> left with the
/// identity provider and every run aborted on a missing directory.
/// </para>
/// <para>
/// The second fact is the sharper one. The runner used to name
/// <c>0002_extensions.sql</c> unconditionally, so a schema needing no extension
/// could not be migrated at all — and the file it demanded is the only one
/// applied as the superuser, so the demand also dragged in a credential the
/// schema never uses.
/// </para>
/// </remarks>
public sealed class MigrationRunnerTargetTests
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

    private static string Runner() => File.ReadAllText(Path.Combine(RepoRoot(), "db", "migrate.sh"));

    [Fact]
    public void TheDefaultMigrationsDirectory_ExistsAndHasMigrations()
    {
        string runner = Runner();

        const string marker = "MIGRATIONS_DIR=\"${MIGRATIONS_DIR:-$SCRIPT_DIR/";
        int start = runner.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "db/migrate.sh no longer defaults MIGRATIONS_DIR under $SCRIPT_DIR.");

        start += marker.Length;
        string relative = runner[start..runner.IndexOf('}', start)];

        string directory = Path.Combine([RepoRoot(), "db", .. relative.Split('/')]);
        Assert.True(
            Directory.Exists(directory),
            $"db/migrate.sh applies db/{relative}, which is not in this repository.");

        Assert.NotEmpty(
            Directory.EnumerateFiles(directory, "????_*.sql").ToArray());
    }

    [Fact]
    public void TheSuperuserExtensionsStep_IsOptional()
    {
        string runner = Runner();

        // And when it is absent the loop must skip nothing: EXT_VERSION is what
        // the apply loop compares against, so leaving it at "0002" would silently
        // drop a 0002 that is some other migration.
        //
        // The assignment has to sit INSIDE the missing-file branch. Asserting the
        // two strings exist somewhere in the file proved neither, and the branch
        // could be deleted with both still present.
        const string branch = "if [ -z \"$EXT_FILE\" ]; then";
        int start = runner.IndexOf(branch, StringComparison.Ordinal);
        Assert.True(start >= 0, "db/migrate.sh no longer guards the absence of an extensions migration.");

        int elif = runner.IndexOf("\nelif ", start, StringComparison.Ordinal);
        Assert.True(elif > start, "the extensions branch has no elif arm; this test is reading the wrong block.");

        Assert.Contains("EXT_VERSION=\"none\"", runner[start..elif], StringComparison.Ordinal);

        // And the apply loop compares against it, which is what makes "none"
        // matching nothing the right value.
        Assert.Contains("[ \"$version\" = \"$EXT_VERSION\" ] && continue", runner, StringComparison.Ordinal);
    }

    // The runner's default is the scaffolding demo's schema, so a deploy that
    // inherited it would create the demo articles table in a production database,
    // skip the product's own migrations, and report success. The count going 0 -> 1
    // also records SCHEMA_CHANGED, which is what rollback.sh refuses on -- so the
    // silent wrong migration would also block the rollback from it.
    [Fact]
    public void TheDeployScript_NamesTheMigrationsDirectoryRatherThanInheritingIt()
    {
        string deploy = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "deploy", "deploy.sh"));

        Assert.Contains("MIGRATIONS_DIR=\"${MIGRATIONS_DIR:-", deploy, StringComparison.Ordinal);
        Assert.Contains("MIGRATIONS_DIR=\"$RELEASE_DIR/$MIGRATIONS_DIR\"", deploy, StringComparison.Ordinal);

        // And it survives both transports to the host half, which is where the
        // runner actually executes.
        Assert.Contains("--env MIGRATIONS_DIR=\"$MIGRATIONS_DIR\"", deploy, StringComparison.Ordinal);
        Assert.Contains("MIGRATIONS_DIR=$(printf %q \"$MIGRATIONS_DIR\")", deploy, StringComparison.Ordinal);
    }

    // The copy a stamped product gets carries the same three guards. It is the
    // live one -- scripts/deploy/ invokes it, not this repo's copy -- so a fix
    // that lands only here is a fix no product receives.
    [Fact]
    public void TheSkeletonRunner_CarriesTheSameGuards()
    {
        string[] expected = Body(Runner())
            .Replace("nativeweb_dev", "__product___dev", StringComparison.Ordinal)
            .Replace("nativeweb_app", "__product___app", StringComparison.Ordinal)
            .Replace("$SCRIPT_DIR/sample/migrations", "$SCRIPT_DIR/migrations", StringComparison.Ordinal)
            .Split('\n');

        string[] actual = Body(File.ReadAllText(Path.Combine(RepoRoot(), "skeleton", "db", "migrate.sh")))
            .Split('\n');

        for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++)
        {
            Assert.True(
                string.Equals(expected[i], actual[i], StringComparison.Ordinal),
                $"skeleton/db/migrate.sh diverges from db/migrate.sh at body line {i + 1}."
                + $"{Environment.NewLine}  db/migrate.sh:          {expected[i]}"
                + $"{Environment.NewLine}  skeleton/db/migrate.sh: {actual[i]}");
        }

        Assert.Equal(expected.Length, actual.Length);
    }

    // The header above `set -euo pipefail` is each file's own documentation: one
    // names this repository's schema, the other carries __PRODUCT__ placeholders.
    // Everything below it is the mechanism, and the mechanism has to be identical.
    private static string Body(string runner)
    {
        int start = runner.IndexOf("set -euo pipefail", StringComparison.Ordinal);
        Assert.True(start >= 0, "a migration runner with no `set -euo pipefail` is not the file this test means.");

        return runner[start..].Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    // It was 0002 in the runner and 0001 on disk, so the file fell through to the
    // main loop and every fresh stamped product failed its first migration run.
    [Fact]
    public void TheSkeletonsExtensionsMigration_MatchesThePatternItsRunnerLooksFor()
    {
        string runner = File.ReadAllText(Path.Combine(RepoRoot(), "skeleton", "db", "migrate.sh"));
        string glob = ExtensionsGlob(runner, "skeleton/db/migrate.sh");

        string[] extensions = ExtensionsMigrations("skeleton", "db", "migrations");
        Assert.NotEmpty(extensions);

        // One, not merely at least one. EXT_VERSION holds a single version, so a
        // second extensions migration falls into the main loop and is applied as
        // the app role -- the same failure with a later number on it. The runner
        // refuses that directory; this asserts the skeleton never ships one.
        Assert.Single(extensions);
        Assert.Contains("Only one file may be applied as $SUPERUSER_ROLE", runner, StringComparison.Ordinal);

        foreach (string? file in extensions)
        {
            Assert.True(
                Regex.IsMatch(file!, GlobToRegex(glob)),
                $"skeleton/db/migrations/{file} creates extensions, which the app role cannot do, "
                    + $"but the runner's superuser step looks for {glob} and would apply it as the app role.");
        }
    }

    // The same drift in this repository's own schema: db/sample has no extensions
    // migration today, so this is a guard rather than a check. Renaming one in
    // would otherwise fall through to the app-role loop with nothing red.
    [Fact]
    public void TheSampleSchemasExtensionsMigrations_MatchThePatternItsRunnerLooksFor()
    {
        string glob = ExtensionsGlob(Runner(), "db/migrate.sh");

        foreach (string? file in ExtensionsMigrations("db", "sample", "migrations"))
        {
            Assert.True(
                Regex.IsMatch(file!, GlobToRegex(glob)),
                $"db/sample/migrations/{file} creates extensions, which the app role cannot do, "
                    + $"but the runner's superuser step looks for {glob} and would apply it as the app role.");
        }

        Assert.True(ExtensionsMigrations("db", "sample", "migrations").Length <= 1);
    }

    private static string ExtensionsGlob(string runner, string path)
    {
        const string marker = "EXT_MATCHES=\"$(compgen -G \"$MIGRATIONS_DIR/";
        int start = runner.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{path} no longer discovers the extensions migration by pattern.");

        start += marker.Length;
        return runner[start..runner.IndexOf('"', start)];
    }

    private static string[] ExtensionsMigrations(params string[] segments) =>
        Directory
            .EnumerateFiles(Path.Combine([RepoRoot(), .. segments]), "*_extensions.sql")
            .Select(Path.GetFileName)
            .ToArray()!;

    private static string GlobToRegex(string glob)
    {
        StringBuilder pattern = new("^");
        for (int i = 0; i < glob.Length; i++)
        {
            if (glob.AsSpan(i).StartsWith("[0-9]", StringComparison.Ordinal))
            {
                pattern.Append("[0-9]");
                i += 4;
            }
            else
            {
                pattern.Append(glob[i] == '*' ? ".*" : Regex.Escape(glob[i].ToString()));
            }
        }

        return pattern.Append('$').ToString();
    }

    // Refused, not merely detected. The substring alone passed with the guard
    // commented out or its result discarded, so it asserted the presence of a line
    // rather than an exit.
    [Fact]
    public void AnEmptyMigrationsDirectory_IsRefusedRatherThanGlobbed()
    {
        string runner = Runner();

        const string guard = "if ! compgen -G \"$MIGRATIONS_DIR/[0-9][0-9][0-9][0-9]_*.sql\" >/dev/null; then";
        int start = runner.IndexOf(guard, StringComparison.Ordinal);
        Assert.True(start >= 0, "db/migrate.sh no longer guards an empty migrations directory with compgen.");

        int end = runner.IndexOf("\nfi", start, StringComparison.Ordinal);
        Assert.True(end > start, "the compgen guard has no closing fi.");

        Assert.Contains("exit 1", runner[start..end], StringComparison.Ordinal);
    }
}
