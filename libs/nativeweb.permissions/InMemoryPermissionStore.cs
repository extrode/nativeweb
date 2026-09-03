using System.Collections.Concurrent;

namespace NativeWeb.Permissions;

/// <summary>
/// An <see cref="IPermissionStore"/> holding everything in dictionaries. The only
/// store this library ships.
/// </summary>
/// <remarks>
/// <para>Here so that the unit tier needs no database and a host can run the access
/// admin before it has written a real store. It is not a cache and not a fallback
/// for one: nothing is persisted, and two hosts in one process each get their own.</para>
/// <para>A PostgreSQL store would be the obvious thing to ship instead, and cannot
/// be: all data access in this stack goes through JauntyQ, whose licence bars a
/// framework library from shipping its generated output, and hand-written ADO.NET
/// is the thing the stack rules exclude. <c>apps/sample</c> writes the real one.</para>
/// </remarks>
public sealed class InMemoryPermissionStore : IPermissionStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _subjects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, string> _groups = [];
    private readonly ConcurrentDictionary<string, byte> _members = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _subjectGrants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _groupGrants = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly Lock _createGroupGate = new();
    private long _nextGroupId;

    /// <summary>Creates an empty store.</summary>
    /// <param name="timeProvider">
    /// Where the first-seen stamp comes from. Injected so a test can assert the
    /// stamp is not overwritten without waiting for a clock to move.
    /// </param>
    public InMemoryPermissionStore(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    /// <summary>When a subject was first seen, or null if it has not been.</summary>
    /// <remarks>Not part of the seam. Here so the unit tier can assert R6 directly.</remarks>
    public DateTimeOffset? FirstSeen(string subject) =>
        _subjects.TryGetValue(subject, out DateTimeOffset seen) ? seen : null;

    /// <inheritdoc/>
    public ValueTask EnsureSubjectAsync(string subject, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ct.ThrowIfCancellationRequested();
        _subjects.TryAdd(subject, _time.GetUtcNow());
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyCollection<string>> GrantedAsync(string subject, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ct.ThrowIfCancellationRequested();

        HashSet<string> held = new(StringComparer.Ordinal);

        foreach (string key in _subjectGrants.Keys)
        {
            if (SplitLengthPrefixed(key) is (string owner, string permission)
                && string.Equals(owner, subject, StringComparison.Ordinal))
            {
                held.Add(permission);
            }
        }

        foreach (long groupId in GroupsOf(subject))
        {
            foreach (string permission in GrantsOfGroup(groupId))
            {
                held.Add(permission);
            }
        }

        return ValueTask.FromResult<IReadOnlyCollection<string>>(held);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<string>> SubjectsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<string> all = [.. _subjects.Keys];
        all.Sort(StringComparer.Ordinal);
        return ValueTask.FromResult<IReadOnlyList<string>>(all);
    }

    /// <inheritdoc/>
    public ValueTask<long> CreateGroupAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ct.ThrowIfCancellationRequested();

        // Under a lock, because the scan and the write are one decision. Without it
        // two concurrent creates of the same name both pass the scan and the store
        // ends with two groups an operator cannot tell apart, where the PostgreSQL
        // store raises PermissionConflictException on the second.
        lock (_createGroupGate)
        {
            if (_groups.Values.Contains(name, StringComparer.Ordinal))
            {
                throw new PermissionConflictException($"A group named {name} already exists.");
            }

            long id = Interlocked.Increment(ref _nextGroupId);
            _groups[id] = name;
            return ValueTask.FromResult(id);
        }
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<PermissionGroup>> GroupsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<PermissionGroup> all = [.. _groups.Select(pair => new PermissionGroup(pair.Key, pair.Value))];
        all.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return ValueTask.FromResult<IReadOnlyList<PermissionGroup>>(all);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<string>> MembersAsync(long groupId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<string> all = [];

        foreach (string key in _members.Keys)
        {
            if (SplitMemberKey(key) is (string subject, long id) && id == groupId)
            {
                all.Add(subject);
            }
        }

        all.Sort(StringComparer.Ordinal);
        return ValueTask.FromResult<IReadOnlyList<string>>(all);
    }

    /// <inheritdoc/>
    public ValueTask AddMemberAsync(string subject, long groupId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ct.ThrowIfCancellationRequested();

        if (!_groups.ContainsKey(groupId))
        {
            throw new PermissionConflictException($"No group with id {groupId}.");
        }

        _members.TryAdd(MemberKey(subject, groupId), 0);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveMemberAsync(string subject, long groupId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ct.ThrowIfCancellationRequested();
        _members.TryRemove(MemberKey(subject, groupId), out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<string>> SubjectGrantsAsync(string subject, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ct.ThrowIfCancellationRequested();

        List<string> all = [];

        foreach (string key in _subjectGrants.Keys)
        {
            if (SplitLengthPrefixed(key) is (string owner, string permission)
                && string.Equals(owner, subject, StringComparison.Ordinal))
            {
                all.Add(permission);
            }
        }

        all.Sort(StringComparer.Ordinal);
        return ValueTask.FromResult<IReadOnlyList<string>>(all);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<string>> GroupGrantsAsync(long groupId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<string> all = [.. GrantsOfGroup(groupId)];
        all.Sort(StringComparer.Ordinal);
        return ValueTask.FromResult<IReadOnlyList<string>>(all);
    }

    /// <inheritdoc/>
    public ValueTask GrantToSubjectAsync(string subject, string permission, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentException.ThrowIfNullOrEmpty(permission);
        ct.ThrowIfCancellationRequested();
        _subjectGrants.TryAdd(SubjectKey(subject, permission), 0);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RevokeFromSubjectAsync(string subject, string permission, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentException.ThrowIfNullOrEmpty(permission);
        ct.ThrowIfCancellationRequested();
        _subjectGrants.TryRemove(SubjectKey(subject, permission), out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask GrantToGroupAsync(long groupId, string permission, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(permission);
        ct.ThrowIfCancellationRequested();

        if (!_groups.ContainsKey(groupId))
        {
            throw new PermissionConflictException($"No group with id {groupId}.");
        }

        _groupGrants.TryAdd(GroupKey(groupId, permission), 0);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RevokeFromGroupAsync(long groupId, string permission, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(permission);
        ct.ThrowIfCancellationRequested();
        _groupGrants.TryRemove(GroupKey(groupId, permission), out _);
        return ValueTask.CompletedTask;
    }

    private IEnumerable<long> GroupsOf(string subject)
    {
        foreach (string key in _members.Keys)
        {
            if (SplitMemberKey(key) is (string owner, long groupId)
                && string.Equals(owner, subject, StringComparison.Ordinal))
            {
                yield return groupId;
            }
        }
    }

    private IEnumerable<string> GrantsOfGroup(long groupId)
    {
        foreach (string key in _groupGrants.Keys)
        {
            if (SplitGroupKey(key) is (long id, string permission) && id == groupId)
            {
                yield return permission;
            }
        }
    }

    // A composite key rather than a nested dictionary, so every mutation is one
    // atomic ConcurrentDictionary operation. The subject is length-prefixed because
    // it is opaque provider text and may contain any character, including whatever
    // separator would otherwise be chosen.
    private static string SubjectKey(string subject, string permission) =>
        $"{subject.Length}:{subject}{permission}";

    private static string MemberKey(string subject, long groupId) =>
        $"{subject.Length}:{subject}{groupId}";

    private static (string Subject, long GroupId)? SplitMemberKey(string key) =>
        SplitLengthPrefixed(key) is (string subject, string tail) && long.TryParse(tail, out long groupId)
            ? (subject, groupId)
            : null;

    private static string GroupKey(long groupId, string permission) =>
        $"{groupId}:{permission}";

    private static (long GroupId, string Permission)? SplitGroupKey(string key)
    {
        int colon = key.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && long.TryParse(key.AsSpan(0, colon), out long groupId)
            ? (groupId, key[(colon + 1)..])
            : null;
    }

    private static (string Head, string Tail)? SplitLengthPrefixed(string key)
    {
        int colon = key.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 0 || !int.TryParse(key.AsSpan(0, colon), out int length))
        {
            return null;
        }

        int start = colon + 1;
        return start + length <= key.Length ? (key.Substring(start, length), key[(start + length)..]) : null;
    }
}
