using Npgsql;

namespace __PRODUCT__.Tests.Integration;

// Side-channel access to the shared __product__ database for asserting on and
// cleaning up rows the endpoints create. Raw Npgsql on purpose: a test
// side-channel doesn't need the generated JauntyQ types, and staying off them
// keeps this file working while the data layer is still being generated.
//
// Connection plumbing only -- the skeleton schema has no domain tables yet.
// The product adds its own query helpers here as it grows tables (see
// NativeWeb's own tests for the shape: one small static method per fact a
// test needs to assert or per row it needs to delete).
internal static class TestDatabase
{
    // Same resolution as the app: CI's env var wins; the fallback matches the
    // local dev user-secrets value.
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("__PRODUCT____Db__ConnectionString")
        ?? "Host=localhost;Database=__product___dev;Username=__product___app";

    // Proof the side-channel actually reaches the database the app is using;
    // also the template for the query helpers the product will add.
    public static async Task<bool> CanConnectAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1", connection);
        return (int)(await cmd.ExecuteScalarAsync() ?? 0) == 1;
    }
}
