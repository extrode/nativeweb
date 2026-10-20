namespace NativeWeb.Permissions;

/// <summary>One group, as the access admin lists it.</summary>
/// <param name="Id">The store's identifier for the group.</param>
/// <param name="Name">The operator-facing name. Free text, unique per store.</param>
public readonly record struct PermissionGroup(long Id, string Name);

/// <summary>
/// The persistence seam every permission decision reads through.
/// </summary>
/// <remarks>
/// <para>The same division <see cref="NativeWeb.Scaffold.IScaffoldStore{T}"/> makes,
/// and for the same reason: NativeWeb has no data layer of its own, so what ships
/// here is a contract and an in-memory implementation, and the application
/// satisfies it with JauntyQ. A framework library may not ship JauntyQ's generated
/// output, so a PostgreSQL store cannot live here even though every real deployment
/// wants one; <c>apps/sample</c> is where that store is written and proved.</para>
/// <para><b>This interface speaks strings, not
/// <see cref="Permission"/>.</b> That is deliberate and it is the one place in the
/// library where a permission is not a typed value. A grant row outlives the
/// declaration that created it: a permission removed from a model leaves rows
/// behind, and the access admin has to list and revoke them after the constant that
/// named them has stopped compiling. A store that took <see cref="Permission"/>
/// could not express a row it can no longer construct. The typing that makes a
/// removed declaration a build error lives one layer up, at
/// <see cref="IPermissions"/> and the endpoint filter, where every caller is
/// application code.</para>
/// <para>Everything takes a <see cref="CancellationToken"/>, for the reason
/// <c>IScaffoldStore</c> states.</para>
/// </remarks>
public interface IPermissionStore
{
    /// <summary>
    /// Records a subject on first sight. Idempotent: called on every request that
    /// carries a subject, and a second call for a known subject changes nothing and
    /// does not overwrite the first-seen stamp.
    /// </summary>
    /// <remarks>
    /// The record holds the opaque subject string and nothing else. No credential,
    /// no display name, no email: the identity provider owns those and they change
    /// under a subject that does not.
    /// </remarks>
    ValueTask EnsureSubjectAsync(string subject, CancellationToken ct);

    /// <summary>
    /// Every permission name this subject holds, directly or through any group it
    /// belongs to, in one call.
    /// </summary>
    /// <remarks>
    /// One call rather than two because it is resolved once per request and the
    /// difference is a round trip on every page. The two sources are unioned here
    /// rather than by the caller, so a store can answer with a single query. Names
    /// no longer declared may appear; the caller drops them against the catalog.
    /// </remarks>
    ValueTask<IReadOnlyCollection<string>> GrantedAsync(string subject, CancellationToken ct);

    /// <summary>Every subject the store has seen, for the access admin's list.</summary>
    ValueTask<IReadOnlyList<string>> SubjectsAsync(CancellationToken ct);

    /// <summary>Creates a group and returns its identifier.</summary>
    /// <remarks>
    /// A name already in use is the store's refusal to make, not the caller's: it
    /// throws <see cref="PermissionConflictException"/>, which the admin renders as
    /// a form error rather than a 500.
    /// </remarks>
    ValueTask<long> CreateGroupAsync(string name, CancellationToken ct);

    /// <summary>Every group, ordered by name.</summary>
    ValueTask<IReadOnlyList<PermissionGroup>> GroupsAsync(CancellationToken ct);

    /// <summary>The subjects in one group.</summary>
    ValueTask<IReadOnlyList<string>> MembersAsync(long groupId, CancellationToken ct);

    /// <summary>Puts a subject in a group. Idempotent.</summary>
    /// <remarks>
    /// A group or a subject the store does not hold is
    /// <see cref="PermissionConflictException"/>, which the admin renders as a form
    /// error. A subject is admitted by <see cref="EnsureSubjectAsync"/> and by
    /// nothing else.
    /// </remarks>
    ValueTask AddMemberAsync(string subject, long groupId, CancellationToken ct);

    /// <summary>Takes a subject out of a group. A subject not in it is not an error.</summary>
    ValueTask RemoveMemberAsync(string subject, long groupId, CancellationToken ct);

    /// <summary>
    /// Removes a group, every membership in it and every permission granted to it.
    /// </summary>
    /// <remarks>
    /// <para>A group the store does not hold is
    /// <see cref="PermissionConflictException"/>, as it is for
    /// <see cref="AddMemberAsync"/> and <see cref="GrantToGroupAsync"/>: an
    /// operator's post naming a group that has gone since the page rendered is a
    /// stale form, not a fault the admin should answer 500 to.</para>
    /// <para>Its members keep everything granted to them directly. Nothing else is
    /// removed: a group is a way of holding permissions, and deleting one is not a
    /// statement about the people who were in it.</para>
    /// </remarks>
    ValueTask DeleteGroupAsync(long groupId, CancellationToken ct);

    /// <summary>
    /// Removes a subject's record, every group membership it holds and every
    /// permission granted to it directly.
    /// </summary>
    /// <remarks>
    /// <para>A subject the store has not seen is
    /// <see cref="PermissionConflictException"/>, for the reason
    /// <see cref="DeleteGroupAsync"/> gives.</para>
    /// <para>The groups it belonged to survive with their other members. So does the
    /// person: this forgets one application's authorization record for a subject
    /// string, and identity belongs to whatever issued it. A subject seen again
    /// afterwards is a new record holding nothing, not a restoration.</para>
    /// </remarks>
    ValueTask ForgetSubjectAsync(string subject, CancellationToken ct);

    /// <summary>The permission names granted to a subject directly, not through a group.</summary>
    ValueTask<IReadOnlyList<string>> SubjectGrantsAsync(string subject, CancellationToken ct);

    /// <summary>The permission names granted to a group.</summary>
    ValueTask<IReadOnlyList<string>> GroupGrantsAsync(long groupId, CancellationToken ct);

    /// <summary>Grants a permission to a subject. Idempotent.</summary>
    /// <remarks>
    /// A subject the store does not hold is
    /// <see cref="PermissionConflictException"/>, as it is for
    /// <see cref="AddMemberAsync"/>.
    /// </remarks>
    ValueTask GrantToSubjectAsync(string subject, string permission, CancellationToken ct);

    /// <summary>Revokes a permission from a subject. Not holding it is not an error.</summary>
    ValueTask RevokeFromSubjectAsync(string subject, string permission, CancellationToken ct);

    /// <summary>Grants a permission to a group. Idempotent.</summary>
    /// <remarks>
    /// A group the store does not hold is <see cref="PermissionConflictException"/>.
    /// </remarks>
    ValueTask GrantToGroupAsync(long groupId, string permission, CancellationToken ct);

    /// <summary>Revokes a permission from a group. Not holding it is not an error.</summary>
    ValueTask RevokeFromGroupAsync(long groupId, string permission, CancellationToken ct);
}
