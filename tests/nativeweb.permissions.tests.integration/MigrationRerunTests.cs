using Npgsql;

using Xunit;

namespace NativeWeb.Permissions.Tests.Integration;

/// <summary>
/// <c>db/migrate.sh</c> re-runs every migration file, so the bootstrap grant in
/// <c>0006_permissions.sql</c> runs again on every deploy. It has to seed the first
/// operator on a fresh database and leave a revoked grant revoked on an old one.
/// </summary>
/// <remarks>
/// In a schema of its own, created and dropped by the test: the file is applied for
/// real rather than read, and applying it to <c>public</c> would rewrite the rows the
/// rest of this tier and the scaffold tier are using.
/// </remarks>
public sealed class MigrationRerunTests
{
    [Fact]
    public async Task ReRunningTheMigration_DoesNotReinstateARevokedGrant()
    {
        string schema = "mig_" + Guid.NewGuid().ToString("N")[..12];
        string sql = await File.ReadAllTextAsync(MigrationPath());

        await using var connection = new NpgsqlConnection(PermissionHost.ConnectionString);
        await connection.OpenAsync();

        try
        {
            await ExecuteAsync(connection, $"CREATE SCHEMA \"{schema}\";");
            await ExecuteAsync(connection, $"SET search_path TO \"{schema}\";");

            await ExecuteAsync(connection, sql);
            Assert.True(await HeldAsync(connection, "demo", "sample:article.delete"));

            await ExecuteAsync(
                connection,
                "DELETE FROM nw_subject_grant WHERE subject = 'demo' AND permission = 'sample:article.delete';");

            await ExecuteAsync(connection, sql);

            Assert.False(await HeldAsync(connection, "demo", "sample:article.delete"));
            Assert.True(await HeldAsync(connection, "demo", "sample:article.view"));
        }
        finally
        {
            await ExecuteAsync(connection, "SET search_path TO public;");
            await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
        }
    }

    [Fact]
    public async Task TheFirstRun_SeedsBothOperators()
    {
        string schema = "mig_" + Guid.NewGuid().ToString("N")[..12];
        string sql = await File.ReadAllTextAsync(MigrationPath());

        await using var connection = new NpgsqlConnection(PermissionHost.ConnectionString);
        await connection.OpenAsync();

        try
        {
            await ExecuteAsync(connection, $"CREATE SCHEMA \"{schema}\";");
            await ExecuteAsync(connection, $"SET search_path TO \"{schema}\";");
            await ExecuteAsync(connection, sql);

            foreach (string subject in (string[])["operator", "demo"])
            {
                foreach (string action in (string[])["view", "add", "change", "delete"])
                {
                    Assert.True(await HeldAsync(connection, subject, $"sample:article.{action}"));
                }
            }
        }
        finally
        {
            await ExecuteAsync(connection, "SET search_path TO public;");
            await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HeldAsync(NpgsqlConnection connection, string subject, string permission)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM nw_subject_grant WHERE subject = @s AND permission = @p;",
            connection);

        command.Parameters.AddWithValue("s", subject);
        command.Parameters.AddWithValue("p", permission);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), provider: null) == 1;
    }

    private static string MigrationPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NativeWeb.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("No NativeWeb.slnx above the test binary.")
            : Path.Combine(directory.FullName, "db", "sample", "migrations", "0006_permissions.sql");
    }
}
