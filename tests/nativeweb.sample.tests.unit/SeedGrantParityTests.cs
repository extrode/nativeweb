using System.Text.RegularExpressions;

using NativeWeb.Permissions;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The sample seeds its first operators twice: <see cref="Seed.Permissions"/> grants
/// them on the in-memory path, and <c>db/sample/migrations/0006_permissions.sql</c>
/// seeds the same names on the database path. A host runs one or the other, so a
/// difference between them is a difference in what the same binary allows.
/// </summary>
/// <remarks>
/// <para>These run the seed rather than restating it. An earlier version compared the
/// migration to <c>ArticlePermissions.All</c> and never touched the seed at all, so
/// it would have stayed green through the revert to <c>PermissionCatalog.All</c>
/// that it was written to catch.</para>
/// <para>The catalog and the article set are equal while <c>Article</c> is the only
/// model carrying <c>[Permissions]</c>, so a revert to the catalog is caught from
/// the moment a second one is declared, and not before. What is held unconditionally
/// is that whatever the seed grants is what the migration grants.</para>
/// </remarks>
public sealed class SeedGrantParityTests
{
    [Fact]
    public async Task TheSeed_GrantsEachSubjectWhatTheMigrationGrantsIt()
    {
        InMemoryPermissionStore store = Seed.Permissions();
        string[] expected = SeededPermissions().OrderBy(name => name, StringComparer.Ordinal).ToArray();

        foreach (string subject in SeededSubjects())
        {
            IReadOnlyCollection<string> granted = await store.GrantedAsync(subject, default);

            Assert.Equal(expected, granted.OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task TheSeed_KnowsTheSubjectsTheMigrationSeeds()
    {
        InMemoryPermissionStore store = Seed.Permissions();

        Assert.Equal(
            SeededSubjects().OrderBy(subject => subject, StringComparer.Ordinal),
            (await store.SubjectsAsync(default)).OrderBy(subject => subject, StringComparer.Ordinal));
    }

    [Fact]
    public void TheMigrationSeeds_MoreThanOnePermission()
    {
        Assert.True(SeededPermissions().Length > 1);
    }

    [Fact]
    public void TheMigrationSeeds_MoreThanOneSubject()
    {
        Assert.True(SeededSubjects().Length > 1);
    }

    private static string[] SeededPermissions() =>
        Quoted(
            Between("CROSS JOIN (VALUES", ") AS p (permission)"),
            "The grant seed's VALUES list names no permission.");

    private static string[] SeededSubjects() =>
        Quoted(
            Between("INSERT INTO nw_subject (subject)", "ON CONFLICT (subject) DO NOTHING"),
            "The subject seed's VALUES list names no subject.");

    private static string[] Quoted(string block, string ifEmpty)
    {
        string[] names = Regex.Matches(block, @"'([^']+)'")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        return names.Length == 0 ? throw new InvalidOperationException(ifEmpty) : names;
    }

    private static string Between(string opening, string closing)
    {
        string sql = File.ReadAllText(
            Path.Combine(RepoRoot(), "db", "sample", "migrations", "0006_permissions.sql"));

        int start = sql.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"0006_permissions.sql has no '{opening}'.");
        }

        int end = sql.IndexOf(closing, start, StringComparison.Ordinal);

        return end < 0
            ? throw new InvalidOperationException($"'{opening}' in 0006_permissions.sql is not closed by '{closing}'.")
            : sql[start..end];
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
