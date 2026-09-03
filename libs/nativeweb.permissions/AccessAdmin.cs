using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NativeWeb.Scaffold;

namespace NativeWeb.Permissions;

/// <summary>
/// Three screens for handing out permissions: groups, one group's members and
/// grants, and one subject's grants. The part of the permission layer a person
/// operates, as against the part a request passes through.
/// </summary>
/// <remarks>
/// <para>Hand-written rather than <c>[Scaffold]</c>-generated. A grant is not a keyed
/// row that <c>IScaffoldStore&lt;T&gt;</c>'s six methods can express, catalog
/// validation has no seam in the generated pipeline, and generating it would force
/// every host to implement three more stores for tables the framework already owns.</para>
/// <para>The look is the admin's, not a second one, but the chrome cannot be
/// referenced: <c>ScaffoldLayout</c> is generated into the host's own assembly, so
/// this library cannot name the type. The host passes its two methods in as
/// delegates instead -- <c>MapAccessAdmin(root, ScaffoldLayout.Page,
/// ScaffoldLayout.AntiforgeryField)</c> -- which is the same chrome without the
/// reference that cannot exist.</para>
/// <para>Every permission name posted is checked against
/// <see cref="PermissionRegistry"/> before it reaches the store, which is half of
/// R14: a name that was never declared cannot be granted through this screen. The
/// other half is what happens to a grant that was made before its declaration was
/// removed -- the row survives, the request-time intersection makes it inert, and
/// this screen lists it as undeclared with a revoke control and no other, so the
/// only thing an operator can do with a dead grant is delete it.</para>
/// </remarks>
public static class AccessAdmin
{
    /// <summary>
    /// Maps the access screens under <paramref name="root"/> at <c>/access</c>.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="root">
    /// The scaffold root, so the screens sit beside the generated admin. The value
    /// passed to <c>[assembly: ScaffoldRoot]</c>, or <c>/admin</c> by default.
    /// </param>
    /// <param name="page">
    /// The host's page chrome, normally <c>ScaffoldLayout.Page</c>: context, title,
    /// body HTML.
    /// </param>
    /// <param name="antiforgeryField">
    /// The host's hidden antiforgery input, normally
    /// <c>ScaffoldLayout.AntiforgeryField</c>. Every form here posts.
    /// </param>
    /// <returns>
    /// The group, so the host can add its own gate. Nothing is applied here: what
    /// guards the access screens is the host's decision, and a framework that
    /// guessed would either lock out the first operator or expose the grant table.
    /// </returns>
    public static RouteGroupBuilder MapAccessAdmin(
        this IEndpointRouteBuilder endpoints,
        string root,
        Func<HttpContext, string, string, IResult> page,
        Func<HttpContext, string> antiforgeryField)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(antiforgeryField);

        string basePath = (root ?? string.Empty).TrimEnd('/') + "/access";
        RouteGroupBuilder group = endpoints.MapGroup(basePath);

        group.MapGet("/", ScaffoldEndpoints.Handle(http => IndexAsync(http, basePath, page, antiforgeryField)));

        group.MapGet(
            "/groups/{id}",
            ScaffoldEndpoints.Handle(http => GroupAsync(http, basePath, page, antiforgeryField)));

        group.MapGet(
            "/subjects/{subject}",
            ScaffoldEndpoints.Handle(http => SubjectAsync(http, basePath, page, antiforgeryField)));

        // RequiresAntiforgery on every one of them. UseAntiforgery validates a
        // request only where the endpoint carries IAntiforgeryMetadata, so without
        // it the token these forms render is decoration and a POST arriving with no
        // token at all is answered 200. Every generated scaffold POST attaches the
        // same metadata, for the same reason.
        group.MapPost("/groups", ScaffoldEndpoints.Handle(http => CreateGroupAsync(http, basePath)))
            .WithMetadata(RequiresAntiforgery.Instance);
        group.MapPost("/groups/{id}/members", ScaffoldEndpoints.Handle(http => AddMemberAsync(http, basePath)))
            .WithMetadata(RequiresAntiforgery.Instance);
        group.MapPost(
                "/groups/{id}/members/remove",
                ScaffoldEndpoints.Handle(http => RemoveMemberAsync(http, basePath)))
            .WithMetadata(RequiresAntiforgery.Instance);
        group.MapPost("/groups/{id}/grants", ScaffoldEndpoints.Handle(http => GrantToGroupAsync(http, basePath)))
            .WithMetadata(RequiresAntiforgery.Instance);
        group.MapPost(
                "/groups/{id}/grants/revoke",
                ScaffoldEndpoints.Handle(http => RevokeFromGroupAsync(http, basePath)))
            .WithMetadata(RequiresAntiforgery.Instance);
        group.MapPost(
                "/subjects/{subject}/grants",
                ScaffoldEndpoints.Handle(http => GrantToSubjectAsync(http, basePath)))
            .WithMetadata(RequiresAntiforgery.Instance);
        group.MapPost(
                "/subjects/{subject}/grants/revoke",
                ScaffoldEndpoints.Handle(http => RevokeFromSubjectAsync(http, basePath)))
            .WithMetadata(RequiresAntiforgery.Instance);

        return group;
    }

    private static async Task<IResult> IndexAsync(
        HttpContext http,
        string basePath,
        Func<HttpContext, string, string, IResult> page,
        Func<HttpContext, string> antiforgeryField)
    {
        IPermissionStore store = Store(http);
        CancellationToken ct = http.RequestAborted;

        IReadOnlyList<PermissionGroup> groups = await store.GroupsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<string> subjects = await store.SubjectsAsync(ct).ConfigureAwait(false);

        var body = new StringBuilder(2048);
        body.Append("<div class=\"nws-head\"><h1>Access</h1></div>");

        body.Append("<h2>Groups</h2><ul class=\"nws-index\">");
        foreach (PermissionGroup entry in groups)
        {
            body.Append("<li><a href=\"").Append(Enc(basePath)).Append("/groups/").Append(entry.Id)
                .Append("\">").Append(Enc(entry.Name)).Append("</a></li>");
        }

        body.Append("</ul>");

        body.Append("<form method=\"post\" action=\"").Append(Enc(basePath)).Append("/groups\">")
            .Append(antiforgeryField(http))
            .Append("<label>Group name <input name=\"name\" required></label>")
            .Append("<button type=\"submit\" class=\"nws-btn nws-btn--primary\">Create group</button></form>");

        body.Append("<h2>Subjects</h2><ul class=\"nws-index\">");
        foreach (string subject in subjects)
        {
            body.Append("<li><a href=\"").Append(Enc(basePath)).Append("/subjects/")
                .Append(Uri.EscapeDataString(subject)).Append("\">").Append(Enc(subject)).Append("</a></li>");
        }

        body.Append("</ul>");

        return page(http, "Access", body.ToString());
    }

    private static async Task<IResult> GroupAsync(
        HttpContext http,
        string basePath,
        Func<HttpContext, string, string, IResult> page,
        Func<HttpContext, string> antiforgeryField)
    {
        IPermissionStore store = Store(http);
        CancellationToken ct = http.RequestAborted;

        long? id = GroupId(http);
        if (id is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<PermissionGroup> groups = await store.GroupsAsync(ct).ConfigureAwait(false);
        PermissionGroup match = groups.FirstOrDefault(g => g.Id == id.Value);
        if (match.Name is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<string> members = await store.MembersAsync(id.Value, ct).ConfigureAwait(false);
        IReadOnlyList<string> grants = await store.GroupGrantsAsync(id.Value, ct).ConfigureAwait(false);

        string self = basePath + "/groups/" + id.Value.ToString(CultureInfo.InvariantCulture);

        var body = new StringBuilder(2048);
        body.Append("<div class=\"nws-head\"><h1>").Append(Enc(match.Name)).Append("</h1></div>");

        body.Append("<h2>Members</h2><ul class=\"nws-index\">");
        foreach (string subject in members)
        {
            body.Append("<li>").Append(Enc(subject))
                .Append("<form method=\"post\" action=\"").Append(Enc(self)).Append("/members/remove\">")
                .Append(antiforgeryField(http))
                .Append("<input type=\"hidden\" name=\"subject\" value=\"").Append(Enc(subject)).Append("\">")
                .Append("<button type=\"submit\" class=\"nws-btn nws-btn--sm nws-btn--danger\">Remove</button>")
                .Append("</form></li>");
        }

        body.Append("</ul>");
        body.Append("<form method=\"post\" action=\"").Append(Enc(self)).Append("/members\">")
            .Append(antiforgeryField(http))
            .Append("<label>Subject <input name=\"subject\" required></label>")
            .Append("<button type=\"submit\" class=\"nws-btn\">Add member</button></form>");

        AppendGrants(body, http, Registry(http), grants, self, antiforgeryField);

        return page(http, match.Name, body.ToString());
    }

    private static async Task<IResult> SubjectAsync(
        HttpContext http,
        string basePath,
        Func<HttpContext, string, string, IResult> page,
        Func<HttpContext, string> antiforgeryField)
    {
        IPermissionStore store = Store(http);
        string subject = ScaffoldEndpoints.RouteValue(http, "subject");

        if (subject.Length == 0)
        {
            return Results.NotFound();
        }

        IReadOnlyList<string> grants =
            await store.SubjectGrantsAsync(subject, http.RequestAborted).ConfigureAwait(false);

        string self = basePath + "/subjects/" + Uri.EscapeDataString(subject);

        var body = new StringBuilder(2048);
        body.Append("<div class=\"nws-head\"><h1>").Append(Enc(subject)).Append("</h1></div>");
        AppendGrants(body, http, Registry(http), grants, self, antiforgeryField);

        return page(http, subject, body.ToString());
    }

    // R14's visible half. A grant whose permission is no longer declared is shown,
    // marked, and revocable -- and nothing else. Hiding it would leave a row nobody
    // could find to delete; offering it in the add list would let it be re-granted.
    private static void AppendGrants(
        StringBuilder body,
        HttpContext http,
        PermissionRegistry registry,
        IReadOnlyList<string> grants,
        string self,
        Func<HttpContext, string> antiforgeryField)
    {
        body.Append("<h2>Permissions</h2><ul class=\"nws-index\">");
        foreach (string grant in grants.OrderBy(g => g, StringComparer.Ordinal))
        {
            body.Append("<li>").Append(Enc(grant));

            if (!registry.Contains(grant))
            {
                body.Append(" <span class=\"nws-undeclared\">undeclared</span>");
            }

            body.Append("<form method=\"post\" action=\"").Append(Enc(self)).Append("/grants/revoke\">")
                .Append(antiforgeryField(http))
                .Append("<input type=\"hidden\" name=\"permission\" value=\"").Append(Enc(grant)).Append("\">")
                .Append("<button type=\"submit\" class=\"nws-btn nws-btn--sm nws-btn--danger\">Revoke</button>")
                .Append("</form></li>");
        }

        body.Append("</ul>");

        body.Append("<form method=\"post\" action=\"").Append(Enc(self)).Append("/grants\">")
            .Append(antiforgeryField(http))
            .Append("<label>Permission <select name=\"permission\">");

        foreach (Permission declared in registry.Declared.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            body.Append("<option value=\"").Append(Enc(declared.Name)).Append("\">")
                .Append(Enc(declared.Name)).Append("</option>");
        }

        body.Append("</select></label><button type=\"submit\" class=\"nws-btn\">Grant</button></form>");
    }

    private static async Task<IResult> CreateGroupAsync(HttpContext http, string basePath)
    {
        string? name = ScaffoldValues.Text(await FormValueAsync(http, "name").ConfigureAwait(false));

        if (name is null)
        {
            return Results.BadRequest();
        }

        return await WrittenAsync(
            async () => await Store(http).CreateGroupAsync(name, http.RequestAborted).ConfigureAwait(false),
            Results.Redirect(basePath + "/")).ConfigureAwait(false);
    }

    private static async Task<IResult> AddMemberAsync(HttpContext http, string basePath)
    {
        long? id = GroupId(http);
        string? subject = ScaffoldValues.Text(await FormValueAsync(http, "subject").ConfigureAwait(false));

        if (id is null || subject is null)
        {
            return Results.BadRequest();
        }

        IPermissionStore store = Store(http);

        return await WrittenAsync(
            async () =>
            {
                await store.EnsureSubjectAsync(subject, http.RequestAborted).ConfigureAwait(false);
                await store.AddMemberAsync(subject, id.Value, http.RequestAborted).ConfigureAwait(false);
            },
            Results.Redirect(GroupPath(basePath, id.Value))).ConfigureAwait(false);
    }

    private static async Task<IResult> RemoveMemberAsync(HttpContext http, string basePath)
    {
        long? id = GroupId(http);
        string? subject = ScaffoldValues.Text(await FormValueAsync(http, "subject").ConfigureAwait(false));

        if (id is null || subject is null)
        {
            return Results.BadRequest();
        }

        await Store(http).RemoveMemberAsync(subject, id.Value, http.RequestAborted).ConfigureAwait(false);

        return Results.Redirect(GroupPath(basePath, id.Value));
    }

    private static async Task<IResult> GrantToGroupAsync(HttpContext http, string basePath)
    {
        long? id = GroupId(http);
        string? permission = await DeclaredPermissionAsync(http).ConfigureAwait(false);

        if (id is null || permission is null)
        {
            return Results.BadRequest();
        }

        return await WrittenAsync(
            () => Store(http).GrantToGroupAsync(id.Value, permission, http.RequestAborted),
            Results.Redirect(GroupPath(basePath, id.Value))).ConfigureAwait(false);
    }

    // Revoking is not validated against the catalog: an undeclared grant is exactly
    // the row this exists to remove, so requiring it to be declared would make dead
    // rows permanent.
    private static async Task<IResult> RevokeFromGroupAsync(HttpContext http, string basePath)
    {
        long? id = GroupId(http);
        string? permission = ScaffoldValues.Text(await FormValueAsync(http, "permission").ConfigureAwait(false));

        if (id is null || permission is null)
        {
            return Results.BadRequest();
        }

        await Store(http).RevokeFromGroupAsync(id.Value, permission, http.RequestAborted).ConfigureAwait(false);

        return Results.Redirect(GroupPath(basePath, id.Value));
    }

    private static async Task<IResult> GrantToSubjectAsync(HttpContext http, string basePath)
    {
        string subject = ScaffoldEndpoints.RouteValue(http, "subject");
        string? permission = await DeclaredPermissionAsync(http).ConfigureAwait(false);

        if (subject.Length == 0 || permission is null)
        {
            return Results.BadRequest();
        }

        IPermissionStore store = Store(http);

        return await WrittenAsync(
            async () =>
            {
                await store.EnsureSubjectAsync(subject, http.RequestAborted).ConfigureAwait(false);
                await store.GrantToSubjectAsync(subject, permission, http.RequestAborted).ConfigureAwait(false);
            },
            Results.Redirect(SubjectPath(basePath, subject))).ConfigureAwait(false);
    }

    private static async Task<IResult> RevokeFromSubjectAsync(HttpContext http, string basePath)
    {
        string subject = ScaffoldEndpoints.RouteValue(http, "subject");
        string? permission = ScaffoldValues.Text(await FormValueAsync(http, "permission").ConfigureAwait(false));

        if (subject.Length == 0 || permission is null)
        {
            return Results.BadRequest();
        }

        await Store(http).RevokeFromSubjectAsync(subject, permission, http.RequestAborted).ConfigureAwait(false);

        return Results.Redirect(SubjectPath(basePath, subject));
    }

    // Every one of these posts names a group or a subject the rendered page saw and
    // the store may no longer have. Both shipped stores raise
    // PermissionConflictException for that, and IPermissionStore documents it as the
    // admin's to render rather than to propagate: unhandled it is a 500 on a form
    // whose only fault is being stale.
    private static async Task<IResult> WrittenAsync(Func<ValueTask> write, IResult onWritten)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (PermissionConflictException)
        {
            return Results.Conflict();
        }

        return onWritten;
    }

    private static async Task<string?> DeclaredPermissionAsync(HttpContext http)
    {
        string? permission = ScaffoldValues.Text(await FormValueAsync(http, "permission").ConfigureAwait(false));

        return permission is not null && Registry(http).Contains(permission) ? permission : null;
    }

    private static async Task<string?> FormValueAsync(HttpContext http, string name)
    {
        if (!http.Request.HasFormContentType)
        {
            return null;
        }

        IFormCollection form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);

        return form[name].ToString();
    }

    private static long? GroupId(HttpContext http) =>
        long.TryParse(
            ScaffoldEndpoints.RouteValue(http, "id"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long id)
            ? id
            : null;

    private static string GroupPath(string basePath, long id) =>
        basePath + "/groups/" + id.ToString(CultureInfo.InvariantCulture);

    private static string SubjectPath(string basePath, string subject) =>
        basePath + "/subjects/" + Uri.EscapeDataString(subject);

    private static IPermissionStore Store(HttpContext http) =>
        http.RequestServices.GetRequiredService<IPermissionStore>();

    private static PermissionRegistry Registry(HttpContext http) =>
        http.RequestServices.GetRequiredService<PermissionRegistry>();

    private static string Enc(string value) => HtmlEncoder.Default.Encode(value);
}
