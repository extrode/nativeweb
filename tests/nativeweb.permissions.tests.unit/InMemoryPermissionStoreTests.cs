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
}
