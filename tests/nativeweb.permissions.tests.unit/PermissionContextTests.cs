using System.Security.Claims;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

public sealed class PermissionContextTests
{
    private static readonly Permission Change = new("sample:article.change");
    private static readonly Permission Delete = new("sample:article.delete");

    // The whole principal a signed-in caller carries here is one claim. Built by
    // hand rather than through the sign-in library, which is R17: the permission
    // layer reads a subject string and does not care who issued it.
    private static ClaimsPrincipal Signed(string subject) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], "test"));

    private static PermissionRegistry Registry() => new([Change, Delete]);

    [Fact]
    public async Task DirectGrant_IsHeld()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        var permissions = new PermissionService(new PermissionContext(store, Registry()));

        Assert.True(await permissions.AllowsAsync(Signed("sub-1"), Change));
        Assert.False(await permissions.AllowsAsync(Signed("sub-1"), Delete));
    }

    [Fact]
    public async Task GroupGrant_ReachesEveryMember()
    {
        var store = new CountingPermissionStore();
        long group = await store.CreateGroupAsync("editors", default);
        await store.EnsureSubjectAsync("sub-1", default);
        await store.AddMemberAsync("sub-1", group, default);
        await store.GrantToGroupAsync(group, Delete.Name, default);
        var permissions = new PermissionService(new PermissionContext(store, Registry()));

        Assert.True(await permissions.AllowsAsync(Signed("sub-1"), Delete));
    }

    [Fact]
    public async Task GrantNamingAnUndeclaredPermission_GrantsNothing()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub-1", "sample:article.publish", default);
        var context = new PermissionContext(store, Registry());

        Assert.Empty(await context.HeldAsync(Signed("sub-1")));
    }

    [Fact]
    public async Task NoSubjectClaim_ReturnsTheEmptySetWithoutTouchingTheStore()
    {
        var store = new CountingPermissionStore();
        var context = new PermissionContext(store, Registry());

        Assert.Empty(await context.HeldAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Equal(0, store.GrantedCalls);
        Assert.Equal(0, store.EnsureSubjectCalls);
    }

    [Fact]
    public async Task HeldSet_IsResolvedOncePerRequest()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        var context = new PermissionContext(store, Registry());
        ClaimsPrincipal user = Signed("sub-1");

        await context.HeldAsync(user);
        await context.HeldAsync(user);
        await context.HeldAsync(user);

        Assert.Equal(1, store.GrantedCalls);
    }

    [Fact]
    public async Task FirstSightOfASubject_RecordsItOnceAndKeepsTheOriginalStamp()
    {
        var clock = new StoppedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new CountingPermissionStore(clock);

        await new PermissionContext(store, Registry()).HeldAsync(Signed("sub-1"));
        DateTimeOffset? first = store.FirstSeen("sub-1");

        clock.Now = clock.Now.AddDays(30);
        await new PermissionContext(store, Registry()).HeldAsync(Signed("sub-1"));

        Assert.NotNull(first);
        Assert.Equal(first, store.FirstSeen("sub-1"));
    }

    [Fact]
    public async Task ADifferentDisplayNameOnTheSameSubject_ChangesNothing()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        var permissions = new PermissionService(new PermissionContext(store, Registry()));

        ClaimsPrincipal renamed = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "sub-1"), new Claim(ClaimTypes.Name, "Someone Else")],
            "test"));

        Assert.True(await permissions.AllowsAsync(renamed, Change));
    }

    [Fact]
    public async Task DefaultPermission_IsNeverAllowed()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        var permissions = new PermissionService(new PermissionContext(store, Registry()));

        Assert.False(await permissions.AllowsAsync(Signed("sub-1"), default));
    }

    [Fact]
    public async Task ResourceId_IsAcceptedAndDoesNotChangeTheAnswer()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        var permissions = new PermissionService(new PermissionContext(store, Registry()));

        Assert.True(await permissions.AllowsAsync(Signed("sub-1"), Change, "row-7"));
    }

    [Fact]
    public async Task ASecondPrincipal_IsAnsweredAboutItself()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.EnsureSubjectAsync("sub-2", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        await store.GrantToSubjectAsync("sub-2", Delete.Name, default);
        var context = new PermissionContext(store, Registry());
        var permissions = new PermissionService(context);

        Assert.True(await permissions.AllowsAsync(Signed("sub-1"), Change));
        Assert.False(await permissions.AllowsAsync(Signed("sub-2"), Change));
        Assert.True(await permissions.AllowsAsync(Signed("sub-2"), Delete));
    }

    [Fact]
    public async Task TwoPrincipalsForOneSubject_ShareTheStoreRead()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        var context = new PermissionContext(store, Registry());

        await context.HeldAsync(Signed("sub-1"));
        await context.HeldAsync(Signed("sub-1"));

        Assert.Equal(1, store.GrantedCalls);
    }

    [Fact]
    public async Task AnIdentityGivenANewSubject_IsAnsweredAboutTheNewOne()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.EnsureSubjectAsync("sub-2", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        await store.GrantToSubjectAsync("sub-2", Delete.Name, default);
        var context = new PermissionContext(store, Registry());

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "sub-1")], "test");
        var user = new ClaimsPrincipal(identity);

        Assert.Equal([Change.Name], await context.HeldAsync(user));

        identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "sub-2"));

        Assert.Equal([Delete.Name], await context.HeldAsync(user));
    }

    [Fact]
    public async Task TwoOverlappingReads_LeaveNeitherSubjectHoldingTheOthersPermissions()
    {
        var store = new CountingPermissionStore();
        await store.EnsureSubjectAsync("sub-1", default);
        await store.EnsureSubjectAsync("sub-2", default);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);
        await store.GrantToSubjectAsync("sub-2", Delete.Name, default);

        TaskCompletionSource entered1 = new(), entered2 = new(), release1 = new(), release2 = new();

        store.BeforeGranted = async subject =>
        {
            if (string.Equals(subject, "sub-1", StringComparison.Ordinal))
            {
                entered1.TrySetResult();
                await release1.Task;
            }
            else
            {
                entered2.TrySetResult();
                await release2.Task;
            }
        };

        var context = new PermissionContext(store, Registry());
        ClaimsPrincipal one = Signed("sub-1"), two = Signed("sub-2");

        Task<IReadOnlySet<string>> first = context.HeldAsync(one).AsTask();
        await entered1.Task;
        Task<IReadOnlySet<string>> second = context.HeldAsync(two).AsTask();
        await entered2.Task;

        release2.SetResult();
        Assert.Equal([Delete.Name], await second);
        release1.SetResult();
        Assert.Equal([Change.Name], await first);

        store.BeforeGranted = null;

        // The same objects, because a memo keyed on the principal reference is
        // answered from the memo here and only here: a fresh principal for sub-2
        // would miss it and read the store, which hides the interleaving.
        Assert.Equal([Delete.Name], await context.HeldAsync(two));
    }

    private sealed class StoppedClock : TimeProvider
    {
        internal StoppedClock(DateTimeOffset now) => Now = now;

        internal DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
