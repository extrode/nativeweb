using NativeWeb.Sample;

using Xunit;

namespace NativeWeb.Permissions.Tests.Integration;

/// <summary>
/// Where the two shipped stores must agree. <see cref="InMemoryPermissionStore"/> is
/// what the unit tier and a starting host run against, and a call it rejects with
/// <see cref="PermissionConflictException"/> cannot come back from
/// <see cref="JauntyPermissionStore"/> as a raw Npgsql exception: a caller written
/// against one seam would then have to catch two types for one input.
/// </summary>
[Collection(PermissionHostCollection.Name)]
public sealed class JauntyStoreSeamTests(PermissionHostFixture fixture)
{
    private const long NoSuchGroup = -1;

    private IPermissionStore Postgres => fixture.Host.Store;

    [Fact]
    public async Task AddingAMemberToAGroupThatDoesNotExist_IsAConflictInBothStores()
    {
        string subject = PermissionHost.NewSubject();
        await Postgres.EnsureSubjectAsync(subject, default);

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await Postgres.AddMemberAsync(subject, NoSuchGroup, default));

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await new InMemoryPermissionStore().AddMemberAsync(subject, NoSuchGroup, default));
    }

    [Fact]
    public async Task AddingAMemberThatIsNotASubject_IsAConflict()
    {
        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await Postgres.AddMemberAsync(PermissionHost.NewSubject(), await GroupAsync(), default));
    }

    [Fact]
    public async Task GrantingToAGroupThatDoesNotExist_IsAConflictInBothStores()
    {
        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await Postgres.GrantToGroupAsync(NoSuchGroup, PermissionHost.View.Name, default));

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await new InMemoryPermissionStore()
                .GrantToGroupAsync(NoSuchGroup, PermissionHost.View.Name, default));
    }

    [Fact]
    public async Task GrantingToASubjectTheStoreHasNotSeen_IsAConflictInBothStores()
    {
        string subject = PermissionHost.NewSubject();

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await Postgres.GrantToSubjectAsync(subject, PermissionHost.View.Name, default));

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await new InMemoryPermissionStore()
                .GrantToSubjectAsync(subject, PermissionHost.View.Name, default));
    }

    [Fact]
    public async Task AddingAMemberTheStoreHasNotSeen_IsAConflictInBothStores()
    {
        var memory = new InMemoryPermissionStore();
        long group = await memory.CreateGroupAsync("editors", default);

        await Assert.ThrowsAsync<PermissionConflictException>(
            async () => await memory.AddMemberAsync(PermissionHost.NewSubject(), group, default));
    }

    [Theory]
    [InlineData("")]
    public async Task AnEmptySubject_IsRejectedByBothStores(string subject)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await Postgres.GrantToSubjectAsync(subject, PermissionHost.View.Name, default));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await new InMemoryPermissionStore()
                .GrantToSubjectAsync(subject, PermissionHost.View.Name, default));
    }

    private async Task<long> GroupAsync() =>
        await Postgres.CreateGroupAsync("seam-" + Guid.NewGuid().ToString("N")[..12], default);
}
