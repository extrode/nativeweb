using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;

namespace NativeWeb.Forms;

/// <summary>
/// Antiforgery metadata for a route that binds a form through a generated binder.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the guard whose absence is silent.</b> Measured, brainstorm 001 §12
/// finding 3: a route bound through <c>IBindableFromHttpContext</c> carries no
/// antiforgery metadata of its own, so <c>UseAntiforgery()</c> — already wired in
/// <c>Program.cs</c> — skips the endpoint entirely, and a form POST with <b>no
/// token at all was answered 200 OK</b>. Nothing logged, nothing warned. Two of the
/// three <c>ContentTypeGuardTests</c> for <c>/auth/register</c> caught it, which is
/// why they exist.
/// </para>
/// <para>
/// The framework's own <c>AntiforgeryMetadata</c> is internal. The interface is
/// public, so this stands in for it — the same approach, and the same reasoning, as
/// <c>NativeWeb.Scaffold.RequiresAntiforgery</c>. Duplicated rather than shared
/// because coupling a general form-binding library to the scaffolding library would
/// make every consumer of one take the other.
/// </para>
/// </remarks>
public sealed class RequiresAntiforgery : IAntiforgeryMetadata
{
    /// <summary>The single instance; it carries no state.</summary>
    public static readonly RequiresAntiforgery Instance = new();

    private RequiresAntiforgery()
    {
    }

    /// <inheritdoc/>
    public bool RequiresValidation => true;
}

/// <summary>Attaches <see cref="RequiresAntiforgery"/> to a route.</summary>
public static class FormRouteBuilderExtensions
{
    /// <summary>
    /// Requires a valid antiforgery token on a route that binds a
    /// <see cref="FormAttribute"/> model.
    /// </summary>
    /// <remarks>
    /// Omitting this call is a build error, not a runtime surprise:
    /// <c>NW014</c> (MissingAntiforgeryAnalyzer) reports any <c>Map*</c> whose
    /// parameter type is a form model and whose chain does not reach here. The
    /// analyzer is the reason this method is safe to make ordinary application code
    /// rather than something the framework applies invisibly — a guard you can see
    /// in the route table is easier to review than one you cannot, provided
    /// forgetting it cannot compile.
    /// </remarks>
    /// <param name="builder">The route being built.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static RouteHandlerBuilder RequireFormAntiforgery(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(RequiresAntiforgery.Instance);
    }
}
