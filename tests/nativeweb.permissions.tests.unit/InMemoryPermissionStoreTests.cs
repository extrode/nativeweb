using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

/// <summary>
/// The shipped store's own facts, where they are about the store rather than about
/// a screen or a filter that happens to use it.
/// </summary>
public sealed class InMemoryPermissionStoreTests
{
    [Fact]
    public async Task ConcurrentCreatesOfOneName_LeaveOneGroup()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var store = new InMemoryPermissionStore();

            Task<bool>[] creates =
            [
                .. Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
                {
                    try
                    {
                        await store.CreateGroupAsync("editors", default);
                        return true;
                    }
                    catch (PermissionConflictException)
                    {
                        return false;
                    }
                })),
            ];

            bool[] outcomes = await Task.WhenAll(creates);

            Assert.Single(outcomes, created => created);
            Assert.Single(await store.GroupsAsync(default));
        }
    }

    [Fact]
    public async Task AMemberAddedToAGroupThatDoesNotExist_IsAConflict()
    {
        var store = new InMemoryPermissionStore();

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await store.AddMemberAsync("sub-1", 404, default));
    }

    [Fact]
    public async Task AGrantToAGroupThatDoesNotExist_IsAConflict()
    {
        var store = new InMemoryPermissionStore();

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await store.GrantToGroupAsync(404, "sample:article.view", default));
    }

    [Fact]
    public async Task DeletingAGroup_TakesItsMembershipsAndItsGrants()
    {
        var store = new InMemoryPermissionStore();
        long group = await store.CreateGroupAsync("editors", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.EnsureSubjectAsync("sub-2", default);
        await store.AddMemberAsync("sub-1", group, default);
        await store.AddMemberAsync("sub-2", group, default);
        await store.GrantToGroupAsync(group, "sample:article.view", default);
        await store.GrantToGroupAsync(group, "sample:article.change", default);

        await store.DeleteGroupAsync(group, default);

        Assert.Empty(await store.GroupsAsync(default));
        Assert.Empty(await store.MembersAsync(group, default));
        Assert.Empty(await store.GroupGrantsAsync(group, default));
        Assert.Empty(await store.GrantedAsync("sub-1", default));
        Assert.Empty(await store.GrantedAsync("sub-2", default));
    }

    [Fact]
    public async Task DeletingAGroup_LeavesItsMembersOwnGrants()
    {
        var store = new InMemoryPermissionStore();
        long group = await store.CreateGroupAsync("editors", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.AddMemberAsync("sub-1", group, default);
        await store.GrantToGroupAsync(group, "sample:article.delete", default);
        await store.GrantToSubjectAsync("sub-1", "sample:article.view", default);

        await store.DeleteGroupAsync(group, default);

        Assert.Equal(["sample:article.view"], await store.GrantedAsync("sub-1", default));
        Assert.Contains("sub-1", await store.SubjectsAsync(default));
    }

    [Fact]
    public async Task DeletingAGroup_LeavesTheOtherGroupsAlone()
    {
        var store = new InMemoryPermissionStore();
        long doomed = await store.CreateGroupAsync("doomed", default);
        long kept = await store.CreateGroupAsync("kept", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.AddMemberAsync("sub-1", doomed, default);
        await store.AddMemberAsync("sub-1", kept, default);
        await store.GrantToGroupAsync(kept, "sample:article.view", default);

        await store.DeleteGroupAsync(doomed, default);

        Assert.Equal(["sub-1"], await store.MembersAsync(kept, default));
        Assert.Equal(["sample:article.view"], await store.GroupGrantsAsync(kept, default));
    }

    [Fact]
    public async Task DeletingAGroupTheStoreDoesNotHold_IsAConflict()
    {
        var store = new InMemoryPermissionStore();

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await store.DeleteGroupAsync(-1, default));
    }

    [Fact]
    public async Task DeletingAGroupTwice_IsAConflictTheSecondTime()
    {
        var store = new InMemoryPermissionStore();
        long group = await store.CreateGroupAsync("editors", default);

        await store.DeleteGroupAsync(group, default);

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await store.DeleteGroupAsync(group, default));
    }

    [Fact]
    public async Task ForgettingASubject_TakesItsMembershipsAndItsGrants()
    {
        var store = new InMemoryPermissionStore();
        long first = await store.CreateGroupAsync("editors", default);
        long second = await store.CreateGroupAsync("reviewers", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.AddMemberAsync("sub-1", first, default);
        await store.AddMemberAsync("sub-1", second, default);
        await store.GrantToSubjectAsync("sub-1", "sample:article.view", default);

        await store.ForgetSubjectAsync("sub-1", default);

        Assert.Empty(await store.SubjectsAsync(default));
        Assert.Empty(await store.MembersAsync(first, default));
        Assert.Empty(await store.MembersAsync(second, default));
        Assert.Empty(await store.SubjectGrantsAsync("sub-1", default));
        Assert.Null(store.FirstSeen("sub-1"));
    }

    [Fact]
    public async Task ForgettingASubject_LeavesTheGroupsItWasIn()
    {
        var store = new InMemoryPermissionStore();
        long group = await store.CreateGroupAsync("editors", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.EnsureSubjectAsync("sub-2", default);
        await store.AddMemberAsync("sub-1", group, default);
        await store.AddMemberAsync("sub-2", group, default);
        await store.GrantToGroupAsync(group, "sample:article.view", default);

        await store.ForgetSubjectAsync("sub-1", default);

        Assert.Equal(["editors"], (await store.GroupsAsync(default)).Select(entry => entry.Name));
        Assert.Equal(["sub-2"], await store.MembersAsync(group, default));
        Assert.Equal(["sample:article.view"], await store.GroupGrantsAsync(group, default));
    }

    [Fact]
    public async Task ASubjectSeenAgainAfterBeingForgotten_HoldsNothing()
    {
        var store = new InMemoryPermissionStore();
        long group = await store.CreateGroupAsync("editors", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.AddMemberAsync("sub-1", group, default);
        await store.GrantToGroupAsync(group, "sample:article.view", default);
        await store.GrantToSubjectAsync("sub-1", "sample:article.delete", default);

        await store.ForgetSubjectAsync("sub-1", default);
        await store.EnsureSubjectAsync("sub-1", default);

        Assert.Empty(await store.GrantedAsync("sub-1", default));
        Assert.Empty(await store.SubjectGrantsAsync("sub-1", default));
    }

    [Fact]
    public async Task ForgettingASubjectTheStoreHasNotSeen_IsAConflict()
    {
        var store = new InMemoryPermissionStore();

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await store.ForgetSubjectAsync("sub-1", default));
    }

    [Fact]
    public async Task ForgettingAnEmptySubject_IsRejected()
    {
        var store = new InMemoryPermissionStore();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.ForgetSubjectAsync("", default));
    }

    [Fact]
    public async Task ForgettingOneSubject_LeavesAnotherWhoseNameSharesAPrefix()
    {
        var store = new InMemoryPermissionStore();
        await store.EnsureSubjectAsync("sub", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub", "sample:article.view", default);
        await store.GrantToSubjectAsync("sub-1", "sample:article.delete", default);

        await store.ForgetSubjectAsync("sub", default);

        Assert.Equal(["sample:article.delete"], await store.SubjectGrantsAsync("sub-1", default));
    }

    [Fact]
    public async Task DeletingGroupOne_LeavesGroupTenAlone()
    {
        var store = new InMemoryPermissionStore();
        long doomed = 0;
        long kept = 0;
        for (int i = 1; i <= 10; i++)
        {
            long group = await store.CreateGroupAsync("group-" + i, default);
            doomed = i == 1 ? group : doomed;
            kept = i == 10 ? group : kept;
        }

        Assert.Equal(1, doomed);
        Assert.Equal(10, kept);

        await store.EnsureSubjectAsync("sub-1", default);
        await store.AddMemberAsync("sub-1", kept, default);
        await store.GrantToGroupAsync(kept, "sample:article.view", default);

        await store.DeleteGroupAsync(doomed, default);

        Assert.Equal(["sub-1"], await store.MembersAsync(kept, default));
        Assert.Equal(["sample:article.view"], await store.GroupGrantsAsync(kept, default));
    }

    [Fact]
    public async Task ForgettingASubject_LeavesASubjectWhoseNameExtendsIt()
    {
        var store = new InMemoryPermissionStore();
        long group = await store.CreateGroupAsync("editors", default);
        await store.EnsureSubjectAsync("sub", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.AddMemberAsync("sub", group, default);
        await store.AddMemberAsync("sub-1", group, default);
        await store.GrantToSubjectAsync("sub", "sample:article.view", default);
        await store.GrantToSubjectAsync("sub-1", "sample:article.view", default);

        await store.ForgetSubjectAsync("sub", default);

        Assert.Equal(["sub-1"], await store.MembersAsync(group, default));
        Assert.Equal(["sample:article.view"], await store.SubjectGrantsAsync("sub-1", default));
        Assert.Empty(await store.SubjectGrantsAsync("sub", default));
    }
}
