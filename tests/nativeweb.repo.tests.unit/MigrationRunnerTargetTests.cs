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

        Assert.Contains("if [ ! -f \"$EXT_FILE\" ]; then", runner, StringComparison.Ordinal);

        // And when it is absent the loop must skip nothing: EXT_VERSION is what
        // the apply loop compares against, so leaving it at "0002" would silently
        // drop a 0002 that is some other migration.
        Assert.Contains("EXT_VERSION=\"none\"", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyMigrationsDirectory_IsRefusedRatherThanGlobbed()
    {
        Assert.Contains(
            "compgen -G \"$MIGRATIONS_DIR/[0-9][0-9][0-9][0-9]_*.sql\"",
            Runner(),
            StringComparison.Ordinal);
    }
}
