namespace NativeWeb.Permissions.Tests.Unit;

/// <summary>
/// An <see cref="InMemoryPermissionStore"/> that counts the calls a test cares
/// about, so "resolved once per request" and "never touched without a subject" are
/// assertions rather than claims.
/// </summary>
internal sealed class CountingPermissionStore : IPermissionStore
{
    private readonly InMemoryPermissionStore _inner;

    internal CountingPermissionStore(TimeProvider? timeProvider = null) =>
        _inner = new InMemoryPermissionStore(timeProvider);

    internal int GrantedCalls { get; private set; }

    internal int EnsureSubjectCalls { get; private set; }

    internal DateTimeOffset? FirstSeen(string subject) => _inner.FirstSeen(subject);

    public ValueTask EnsureSubjectAsync(string subject, CancellationToken ct)
    {
        EnsureSubjectCalls++;
        return _inner.EnsureSubjectAsync(subject, ct);
    }

    // Lets a test hold one read open while another starts, which is the only way
    // to reach the interleaving PermissionContext memoises against.
    internal Func<string, Task>? BeforeGranted { get; set; }

    public async ValueTask<IReadOnlyCollection<string>> GrantedAsync(string subject, CancellationToken ct)
    {
        GrantedCalls++;

        if (BeforeGranted is not null)
        {
            await BeforeGranted(subject).ConfigureAwait(false);
        }

        return await _inner.GrantedAsync(subject, ct).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<string>> SubjectsAsync(CancellationToken ct) => _inner.SubjectsAsync(ct);

    public ValueTask DeleteGroupAsync(long groupId, CancellationToken ct) => _inner.DeleteGroupAsync(groupId, ct);

    public ValueTask ForgetSubjectAsync(string subject, CancellationToken ct) =>
        _inner.ForgetSubjectAsync(subject, ct);

    public ValueTask<long> CreateGroupAsync(string name, CancellationToken ct) => _inner.CreateGroupAsync(name, ct);

    public ValueTask<IReadOnlyList<PermissionGroup>> GroupsAsync(CancellationToken ct) => _inner.GroupsAsync(ct);

    public ValueTask<IReadOnlyList<string>> MembersAsync(long groupId, CancellationToken ct) =>
        _inner.MembersAsync(groupId, ct);

    public ValueTask AddMemberAsync(string subject, long groupId, CancellationToken ct) =>
        _inner.AddMemberAsync(subject, groupId, ct);

    public ValueTask RemoveMemberAsync(string subject, long groupId, CancellationToken ct) =>
        _inner.RemoveMemberAsync(subject, groupId, ct);

    public ValueTask<IReadOnlyList<string>> SubjectGrantsAsync(string subject, CancellationToken ct) =>
        _inner.SubjectGrantsAsync(subject, ct);

    public ValueTask<IReadOnlyList<string>> GroupGrantsAsync(long groupId, CancellationToken ct) =>
        _inner.GroupGrantsAsync(groupId, ct);

    public ValueTask GrantToSubjectAsync(string subject, string permission, CancellationToken ct) =>
        _inner.GrantToSubjectAsync(subject, permission, ct);

    public ValueTask RevokeFromSubjectAsync(string subject, string permission, CancellationToken ct) =>
        _inner.RevokeFromSubjectAsync(subject, permission, ct);

    public ValueTask GrantToGroupAsync(long groupId, string permission, CancellationToken ct) =>
        _inner.GrantToGroupAsync(groupId, permission, ct);

    public ValueTask RevokeFromGroupAsync(long groupId, string permission, CancellationToken ct) =>
        _inner.RevokeFromGroupAsync(groupId, permission, ct);
}
