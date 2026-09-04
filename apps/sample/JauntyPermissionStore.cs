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

            return created.Count > 0
                ? created[0].Id
                : throw new PermissionConflictException($"Creating the group {name} returned no row.");
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
        ArgumentException.ThrowIfNullOrEmpty(subject);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        try
        {
            await db.Permissions.AddMemberAsync(subject, groupId, ct).ConfigureAwait(false);
        }
        catch (PostgresException missing) when (IsMissingReference(missing))
        {
            throw new PermissionConflictException(MissingReference(missing, subject, groupId));
        }
    }

    public async ValueTask RemoveMemberAsync(string subject, long groupId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.RemoveMemberAsync(subject, groupId, ct).ConfigureAwait(false);
    }

    public async ValueTask DeleteGroupAsync(long groupId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<PermissionQueries.Result.DeleteGroup> deleted =
            await db.Permissions.DeleteGroupAsync(groupId, ct).ConfigureAwait(false);

        // No row came back, so there was none to delete. The in-memory store answers
        // the same input the same way; one seam must not hand the caller a silent
        // success here and a conflict there.
        if (deleted.Count == 0)
        {
            throw new PermissionConflictException($"No group with id {groupId}.");
        }
    }

    public async ValueTask ForgetSubjectAsync(string subject, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<PermissionQueries.Result.ForgetSubject> forgotten =
            await db.Permissions.ForgetSubjectAsync(subject, ct).ConfigureAwait(false);

        if (forgotten.Count == 0)
        {
            throw new PermissionConflictException($"No subject {subject}.");
        }
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
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentException.ThrowIfNullOrEmpty(permission);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        try
        {
            await db.Permissions.GrantToSubjectAsync(subject, permission, ct).ConfigureAwait(false);
        }
        catch (PostgresException missing) when (IsMissingReference(missing))
        {
            throw new PermissionConflictException($"No subject {subject}.");
        }
    }

    public async ValueTask RevokeFromSubjectAsync(string subject, string permission, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Permissions.RevokeFromSubjectAsync(subject, permission, ct).ConfigureAwait(false);
    }

    public async ValueTask GrantToGroupAsync(long groupId, string permission, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(permission);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        try
        {
            await db.Permissions.GrantToGroupAsync(groupId, permission, ct).ConfigureAwait(false);
        }
        catch (PostgresException missing) when (IsMissingReference(missing))
        {
            throw new PermissionConflictException($"No group with id {groupId}.");
        }
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
    // A row naming a group or a subject that does not exist is the same input the
    // in-memory store rejects with PermissionConflictException, and one seam must not
    // hand the caller two different exception types for it: without this the caller
    // sees a raw Npgsql exception on PostgreSQL and a conflict in the unit tier.
    internal static bool IsMissingReference(PostgresException e) =>
        e.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    private static string MissingReference(PostgresException e, string subject, long groupId) =>
        e.ConstraintName?.EndsWith("_subject_fkey", StringComparison.Ordinal) == true
            ? $"No subject {subject}."
            : $"No group with id {groupId}.";

    internal static bool IsNameTaken(PostgresException e) =>
        e.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(e.ConstraintName, "nw_group_name_key", StringComparison.Ordinal);
}
