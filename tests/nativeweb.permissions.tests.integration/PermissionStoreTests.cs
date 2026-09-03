using System.Net;

using Xunit;

namespace NativeWeb.Permissions.Tests.Integration;

/// <summary>
/// One application, started once, shared by every test in the collection.
/// </summary>
/// <remarks>
/// Per-test would mean sixteen data sources against the same database for a suite
/// whose tests are isolated by subject string rather than by connection.
/// </remarks>
public sealed class PermissionHostFixture : IAsyncLifetime
{
    internal PermissionHost Host { get; private set; } = null!;

    public async Task InitializeAsync() => Host = await PermissionHost.StartAsync();

    public async Task DisposeAsync() => await Host.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PermissionHostCollection : ICollectionFixture<PermissionHostFixture>
{
    public const string Name = "permission-host";
}

/// <summary>
/// The permission layer against the real <c>nw_</c> tables in
/// <c>nativeweb_sample_test</c>, through <c>JauntyPermissionStore</c>.
/// </summary>
[Collection(PermissionHostCollection.Name)]
public sealed class PermissionStoreTests(PermissionHostFixture fixture)
{
    private PermissionHost Host => fixture.Host;

    [Fact]
    public async Task FirstSightOfASubject_WritesTheSubjectRow()
    {
        string subject = PermissionHost.NewSubject();

        Assert.Null(await PermissionHost.FirstSeenAsync(subject));

        using HttpResponseMessage response = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(await PermissionHost.FirstSeenAsync(subject));
    }

    [Fact]
    public async Task ASecondRequest_LeavesTheFirstSeenStampAlone()
    {
        string subject = PermissionHost.NewSubject();

        using (await Host.GetAsync("/change", subject)) { }

        DateTimeOffset? first = await PermissionHost.FirstSeenAsync(subject);

        using (await Host.GetAsync("/change", subject)) { }

        Assert.Equal(first, await PermissionHost.FirstSeenAsync(subject));
    }

    [Fact]
    public async Task AnAnonymousRequest_ToAGatedEndpoint_Is401()
    {
        using HttpResponseMessage response = await Host.GetAsync("/change", subject: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ASignedInSubjectWithoutTheGrant_Is403()
    {
        using HttpResponseMessage response = await Host.GetAsync("/change", PermissionHost.NewSubject());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnUngatedEndpoint_AdmitsASubjectHoldingNothing()
    {
        using HttpResponseMessage response = await Host.GetAsync("/open", PermissionHost.NewSubject());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ADirectGrant_Admits()
    {
        string subject = PermissionHost.NewSubject();
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.GrantToSubjectAsync(subject, PermissionHost.Change.Name, default);

        using HttpResponseMessage response = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AGrantOfADifferentPermission_DoesNotAdmit()
    {
        string subject = PermissionHost.NewSubject();
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.GrantToSubjectAsync(subject, PermissionHost.View.Name, default);

        using HttpResponseMessage response = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AGroupGrant_Admits()
    {
        string subject = PermissionHost.NewSubject();
        long group = await Host.Store.CreateGroupAsync("editors-" + subject, default);
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.AddMemberAsync(subject, group, default);
        await Host.Store.GrantToGroupAsync(group, PermissionHost.Change.Name, default);

        using HttpResponseMessage response = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemovingTheMember_WithdrawsWhatTheGroupGranted()
    {
        string subject = PermissionHost.NewSubject();
        long group = await Host.Store.CreateGroupAsync("editors-" + subject, default);
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.AddMemberAsync(subject, group, default);
        await Host.Store.GrantToGroupAsync(group, PermissionHost.Change.Name, default);

        using (HttpResponseMessage admitted = await Host.GetAsync("/change", subject))
        {
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        }

        await Host.Store.RemoveMemberAsync(subject, group, default);

        using HttpResponseMessage refused = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task ANameOutsideTheCatalog_IsNeverHeld()
    {
        string subject = PermissionHost.NewSubject();
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.GrantToSubjectAsync(subject, "sample:ghost.change", default);

        Assert.Contains("sample:ghost.change", await Host.Store.SubjectGrantsAsync(subject, default));

        using HttpResponseMessage response = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(true, true, HttpStatusCode.OK)]
    [InlineData(true, false, HttpStatusCode.Forbidden)]
    [InlineData(false, true, HttpStatusCode.Forbidden)]
    [InlineData(false, false, HttpStatusCode.Forbidden)]
    public async Task TheRoleAndThePermission_BothHaveToPass(
        bool hasRole, bool hasGrant, HttpStatusCode expected)
    {
        string subject = PermissionHost.NewSubject();
        await Host.Store.EnsureSubjectAsync(subject, default);

        if (hasGrant)
        {
            await Host.Store.GrantToSubjectAsync(subject, PermissionHost.Change.Name, default);
        }

        using HttpResponseMessage response =
            await Host.GetAsync("/editors-only", subject, hasRole ? "editor" : "reader");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/rows/1")]
    [InlineData("/rows/2")]
    public async Task AResourceRoute_DecidesAtModelLevel_SoEveryRowAnswersAlike(string path)
    {
        string subject = PermissionHost.NewSubject();
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.GrantToSubjectAsync(subject, PermissionHost.Change.Name, default);

        using HttpResponseMessage response = await Host.GetAsync(path, subject);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AGrantMadeThroughTheAccessAdmin_AdmitsTheNextRequest()
    {
        string subject = PermissionHost.NewSubject();

        using (HttpResponseMessage refused = await Host.GetAsync("/change", subject))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using HttpResponseMessage granted = await Host.PostAsync(
            $"/admin/access/subjects/{subject}/grants",
            subject,
            [new KeyValuePair<string, string>("permission", PermissionHost.Change.Name)]);

        Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);

        using HttpResponseMessage admitted = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    [Fact]
    public async Task ARevocationThroughTheAccessAdmin_RefusesTheNextRequest()
    {
        string subject = PermissionHost.NewSubject();
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.GrantToSubjectAsync(subject, PermissionHost.Change.Name, default);

        using HttpResponseMessage revoked = await Host.PostAsync(
            $"/admin/access/subjects/{subject}/grants/revoke",
            subject,
            [new KeyValuePair<string, string>("permission", PermissionHost.Change.Name)]);

        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);

        using HttpResponseMessage refused = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(await Host.Store.SubjectGrantsAsync(subject, default));
    }

    [Fact]
    public async Task AGroupBuiltThroughTheAccessAdmin_AdmitsItsMember()
    {
        string subject = PermissionHost.NewSubject();
        string name = "editors-" + subject;

        using (HttpResponseMessage created = await Host.PostAsync(
            "/admin/access/groups", subject, [new KeyValuePair<string, string>("name", name)]))
        {
            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        }

        IReadOnlyList<PermissionGroup> groups = await Host.Store.GroupsAsync(default);
        PermissionGroup group = Assert.Single(groups, g => g.Name == name);

        using (HttpResponseMessage member = await Host.PostAsync(
            $"/admin/access/groups/{group.Id}/members",
            subject,
            [new KeyValuePair<string, string>("subject", subject)]))
        {
            Assert.Equal(HttpStatusCode.Redirect, member.StatusCode);
        }

        using (HttpResponseMessage grant = await Host.PostAsync(
            $"/admin/access/groups/{group.Id}/grants",
            subject,
            [new KeyValuePair<string, string>("permission", PermissionHost.Change.Name)]))
        {
            Assert.Equal(HttpStatusCode.Redirect, grant.StatusCode);
        }

        using HttpResponseMessage admitted = await Host.GetAsync("/change", subject);

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    [Fact]
    public async Task TheAccessAdmin_RefusesAGrantOfAnUndeclaredName()
    {
        string subject = PermissionHost.NewSubject();

        using HttpResponseMessage response = await Host.PostAsync(
            $"/admin/access/subjects/{subject}/grants",
            subject,
            [new KeyValuePair<string, string>("permission", "sample:ghost.change")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheSubjectPage_ShowsTheGrantTheAdminJustMade()
    {
        string subject = PermissionHost.NewSubject();

        using (await Host.PostAsync(
            $"/admin/access/subjects/{subject}/grants",
            subject,
            [new KeyValuePair<string, string>("permission", PermissionHost.View.Name)]))
        { }

        using HttpResponseMessage page = await Host.GetAsync($"/admin/access/subjects/{subject}", subject);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        string html = await page.Content.ReadAsStringAsync();

        Assert.Contains(PermissionHost.View.Name, html, StringComparison.Ordinal);
        Assert.Equal([PermissionHost.View.Name], await Host.Store.SubjectGrantsAsync(subject, default));
    }

    [Fact]
    public async Task AnUndeclaredGrantRow_IsListedAsUndeclared()
    {
        string subject = PermissionHost.NewSubject();
        await Host.Store.EnsureSubjectAsync(subject, default);
        await Host.Store.GrantToSubjectAsync(subject, "sample:ghost.change", default);

        using HttpResponseMessage page = await Host.GetAsync($"/admin/access/subjects/{subject}", subject);

        string html = await page.Content.ReadAsStringAsync();

        Assert.Contains("nws-undeclared", html, StringComparison.Ordinal);
    }
}
