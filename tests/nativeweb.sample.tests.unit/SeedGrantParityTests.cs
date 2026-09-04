using System.Text.RegularExpressions;

using NativeWeb.Permissions;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The sample seeds its first operators twice: <c>Program.cs</c> grants
/// <c>ArticlePermissions.All</c> to the in-memory store, and
/// <c>db/sample/migrations/0006_permissions.sql</c> seeds four literal names on the
/// database path. A host runs one or the other, so a difference between them is a
/// difference in what the same binary allows.
/// </summary>
/// <remarks>
/// Program.cs used to grant <c>PermissionCatalog.All</c>, which is every permission
/// the assembly declares rather than the four the migration names. Identical while
/// <c>Article</c> is the only model carrying <c>[Permissions]</c>, and divergent the
/// moment a second one arrives.
/// </remarks>
public sealed class SeedGrantParityTests
{
    [Fact]
    public void TheMigrationSeeds_ExactlyTheArticlePermissions()
    {
        Assert.Equal(
            ArticlePermissions.All.Select(permission => permission.Name).OrderBy(name => name, StringComparer.Ordinal),
            SeededByTheMigration().OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void TheMigrationSeeds_MoreThanOnePermission()
    {
        Assert.True(SeededByTheMigration().Length > 1);
    }

    private static string[] SeededByTheMigration()
    {
        string sql = File.ReadAllText(
            Path.Combine(RepoRoot(), "db", "sample", "migrations", "0006_permissions.sql"));

        int cross = sql.IndexOf("CROSS JOIN (VALUES", StringComparison.Ordinal);

        if (cross < 0)
        {
            throw new InvalidOperationException("0006_permissions.sql has no CROSS JOIN (VALUES for the grant seed.");
        }

        int close = sql.IndexOf(") AS p (permission)", cross, StringComparison.Ordinal);

        if (close < 0)
        {
            throw new InvalidOperationException("The grant seed's VALUES list is not closed by ') AS p (permission)'.");
        }

        string[] names = Regex.Matches(sql[cross..close], @"'([^']+)'")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        return names.Length == 0
            ? throw new InvalidOperationException("The grant seed's VALUES list names no permission.")
            : names;
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NativeWeb.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"No NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }
}
