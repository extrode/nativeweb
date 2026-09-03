using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The permission schema exists twice: applied at
/// <c>db/sample/migrations/0006_permissions.sql</c>, and quoted as a reference at
/// <c>docs/specs/024-model-permissions/024-schema.sql</c> for an application
/// adopting the layer.
/// </summary>
/// <remarks>
/// The reference file says the table DDL is identical character for character, and
/// that claim is the reason this test exists. Two copies of a schema drift the
/// moment one of them is edited, and the one that drifts is the documented one --
/// which is then wrong for exactly the reader who has no other copy to check
/// against. Comments and the bootstrap block deliberately differ and are not
/// compared; the statements are.
/// </remarks>
public sealed class PermissionSchemaParityTests
{
    private const string Applied = "db/sample/migrations/0006_permissions.sql";
    private const string Reference = "docs/specs/024-model-permissions/024-schema.sql";

    [Fact]
    public void TheReferenceSchema_DeclaresTheSameStatementsAsTheMigration()
    {
        Assert.Equal(Statements(Applied), Statements(Reference));
    }

    [Fact]
    public void TheMigration_DeclaresTheFiveTablesTheStoreReads()
    {
        string[] tables =
        [
            "nw_subject", "nw_group", "nw_group_member", "nw_subject_grant", "nw_group_grant",
        ];

        IReadOnlyList<string> statements = Statements(Applied);

        foreach (string table in tables)
        {
            Assert.Contains(statements, s => s.StartsWith($"CREATE TABLE IF NOT EXISTS {table} (", StringComparison.Ordinal));
        }
    }

    // Without a seeded grant nobody can reach the access admin to make the first
    // one, so the migration that omits it locks the application out permanently.
    [Fact]
    public void TheMigration_SeedsTheFirstOperator()
    {
        string applied = File.ReadAllText(Path.Combine(RepoRoot(), Applied));

        Assert.Contains("INSERT INTO nw_subject_grant", applied, StringComparison.Ordinal);
        Assert.Contains("'sample:article.delete'", applied, StringComparison.Ordinal);
    }

    // The reference is documentation. A seed naming the sample's own subjects would
    // be copied into an application where those subjects mean nothing.
    [Fact]
    public void TheReferenceSchema_SeedsNobody()
    {
        string reference = File.ReadAllText(Path.Combine(RepoRoot(), Reference));

        Assert.DoesNotContain("INSERT INTO", Uncommented(reference), StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Statements(string relativePath)
    {
        string text = Uncommented(File.ReadAllText(Path.Combine(RepoRoot(), relativePath)));

        return
        [
            .. text.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(statement => string.Join(
                    '\n',
                    statement.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0)))
                .Where(statement => statement.Length > 0)
                // DDL only. The applied file's bootstrap is a CTE around an INSERT
                // rather than a bare INSERT, so excluding the seed by its leading
                // keyword stopped excluding it; excluding everything that is not a
                // CREATE says what the comparison is actually about.
                .Where(statement => statement.StartsWith("CREATE ", StringComparison.Ordinal)),
        ];
    }

    private static string Uncommented(string sql) =>
        string.Join(
            '\n',
            sql.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

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
