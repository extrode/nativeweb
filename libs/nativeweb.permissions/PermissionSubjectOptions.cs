using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// Which claim, or which function, names the subject a permission decision is made
/// about.
/// </summary>
/// <remarks>
/// <para>Configured once, at registration:
/// <c>services.AddPermissions(PermissionCatalog.All, options =&gt; options.ClaimName = "sub")</c>.
/// A host that configures nothing gets <see cref="ClaimTypes.NameIdentifier"/>,
/// which is what every application on this layer had before this type existed.</para>
/// <para>The subject is whatever comes back, unchanged. Nothing here trims, lowers or
/// canonicalises it: it is the key of every grant row, every membership and every
/// <c>nw_subject</c> record, so a normalisation applied on the way in would have to
/// be applied identically by every store, every migration and every operator typing
/// a subject into the access admin.</para>
/// </remarks>
public sealed class PermissionSubjectOptions
{
    /// <summary>
    /// The claim carrying the subject. Defaults to
    /// <see cref="ClaimTypes.NameIdentifier"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ClaimTypes.NameIdentifier"/> is the WS-Fed URI, not the string
    /// <c>nameidentifier</c>. A JWT bearer scheme with <c>MapInboundClaims</c> off
    /// leaves <c>sub</c> as <c>sub</c>, and this is where such a host says so.
    /// </remarks>
    public string ClaimName { get; set; } = ClaimTypes.NameIdentifier;

    /// <summary>
    /// A function from an authenticated principal to its subject, for a subject that
    /// is not any single claim. Null selects <see cref="ClaimName"/>.
    /// </summary>
    /// <remarks>
    /// <para>Runs on the first permission decision of each request, on the request
    /// thread, and its answer is what reaches the store. Return null or whitespace
    /// for a principal it cannot name; that is the same outcome as an absent claim.</para>
    /// <para>Exceptions are not caught. A resolver that throws fails the request,
    /// which is louder than a refusal and easier to find than a 403 with no cause.</para>
    /// </remarks>
    public Func<ClaimsPrincipal, string?>? Resolve { get; set; }
}
