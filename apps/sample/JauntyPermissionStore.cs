using JauntyQ.Generated;

using NativeWeb.Permissions;

using Npgsql;

// The generated query class is JauntyQ.Generated.Permissions, and the library's
// namespace is NativeWeb.Permissions: unaliased, `Permissions.Result` binds to the
// namespace and every result type is CS0234. Aliased for the same reason
// JauntyArticleStore aliases its row types.
using PermissionQueries = JauntyQ.Generated.Permissions;

namespace NativeWeb.Sample;

/// <summary>
/// <see cref="IPermissionStore"/> over the generated <c>db.Permissions.*</c>
/// methods from <c>db/sample/queries/Permissions/</c> (spec 024 R6).
/// </summary>
/// <remarks>
/// <para>The tables are <c>db/sample/migrations/0006_permissions.sql</c>, and they
/// belong to this application rather than to the framework: NativeWeb defines the
/// seam and ships no migration runner, so an application adopting the permission
/// layer stamps the schema and writes a store like this one.</para>
/// <para>Singleton-safe for the reason the article and tag stores are: a connection
/// per call and no per-request state. What is memoised per request is the held set,
/// and that lives in <c>IPermissionContext</c>, one layer up.</para>
/// </remarks>
internal sealed class JauntyPermissionStore(NpgsqlDataSource dataSource) : IPermissionStore
{
    public async ValueTask EnsureSubjectAsync(string subject, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.EnsureSubjectAsync(subject, ct).ConfigureAwait(false);
    }

    // Two reads rather than one. GrantedDirect.sql says why: JauntyQ refuses set
    // operations, and the join-shaped single query is a cross product of the two
    // sets. Both run on one connection, and the caller memoises the answer for the
    // request, so a page with six gated controls still resolves this once.
    public async ValueTask<IReadOnlyCollection<string>> GrantedAsync(string subject, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<PermissionQueries.Result.GrantedDirect> direct =
            await db.Permissions.GrantedDirectAsync(subject, ct).ConfigureAwait(false);
        List<PermissionQueries.Result.GrantedThroughGroups> viaGroups =
            await db.Permissions.GrantedThroughGroupsAsync(subject, ct).ConfigureAwait(false);

        var held = new HashSet<string>(direct.Count + viaGroups.Count, StringComparer.Ordinal);
        foreach (PermissionQueries.Result.GrantedDirect row in direct)
        {
            held.Add(row.Permission);
        }

        foreach (PermissionQueries.Result.GrantedThroughGroups row in viaGroups)
        {
            held.Add(row.Permission);
        }

        return held;
    }

    public async ValueTask<IReadOnlyList<string>> SubjectsAsync(CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<PermissionQueries.Result.Subjects> rows = await db.Permissions.SubjectsAsync(ct).ConfigureAwait(false);

        return [.. rows.Select(row => row.Subject)];
    }

    public async ValueTask<long> CreateGroupAsync(string name, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        try
        {
            List<PermissionQueries.Result.CreateGroup> created =
                await db.Permissions.CreateGroupAsync(name, ct).ConfigureAwait(false);

            return created[0].Id;
        }
        catch (PostgresException taken) when (IsNameTaken(taken))
        {
            throw new PermissionConflictException($"A group named {name} already exists.");
        }
    }

    public async ValueTask<IReadOnlyList<PermissionGroup>> GroupsAsync(CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<NwGroupRow> rows = await db.Permissions.GroupsAsync(ct).ConfigureAwait(false);

        return [.. rows.Select(row => new PermissionGroup(row.Id, row.Name))];
    }

    public async ValueTask<IReadOnlyList<string>> MembersAsync(long groupId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<PermissionQueries.Result.Members> rows =
            await db.Permissions.MembersAsync(groupId, ct).ConfigureAwait(false);

        return [.. rows.Select(row => row.Subject)];
    }

    public async ValueTask AddMemberAsync(string subject, long groupId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.AddMemberAsync(subject, groupId, ct).ConfigureAwait(false);
    }

    public async ValueTask RemoveMemberAsync(string subject, long groupId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.RemoveMemberAsync(subject, groupId, ct).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<string>> SubjectGrantsAsync(string subject, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<PermissionQueries.Result.SubjectGrants> rows =
            await db.Permissions.SubjectGrantsAsync(subject, ct).ConfigureAwait(false);

        return [.. rows.Select(row => row.Permission)];
    }

    public async ValueTask<IReadOnlyList<string>> GroupGrantsAsync(long groupId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<PermissionQueries.Result.GroupGrants> rows =
            await db.Permissions.GroupGrantsAsync(groupId, ct).ConfigureAwait(false);

        return [.. rows.Select(row => row.Permission)];
    }

    public async ValueTask GrantToSubjectAsync(string subject, string permission, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.GrantToSubjectAsync(subject, permission, ct).ConfigureAwait(false);
    }

    public async ValueTask RevokeFromSubjectAsync(string subject, string permission, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.RevokeFromSubjectAsync(subject, permission, ct).ConfigureAwait(false);
    }

    public async ValueTask GrantToGroupAsync(long groupId, string permission, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.GrantToGroupAsync(groupId, permission, ct).ConfigureAwait(false);
    }

    public async ValueTask RevokeFromGroupAsync(long groupId, string permission, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.RevokeFromGroupAsync(groupId, permission, ct).ConfigureAwait(false);
    }

    // The constraint is named, not merely the SQL state: nw_group has one unique
    // constraint today, and a second one arriving later must not be reported as a
    // duplicate name.
    internal static bool IsNameTaken(PostgresException e) =>
        e.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(e.ConstraintName, "nw_group_name_key", StringComparison.Ordinal);
}
