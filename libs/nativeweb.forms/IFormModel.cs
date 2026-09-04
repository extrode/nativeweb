using Microsoft.AspNetCore.Http;

namespace NativeWeb.Forms;

/// <summary>
/// A form model that can bind itself, implemented by the generated partial.
/// </summary>
/// <typeparam name="TSelf">The model itself.</typeparam>
/// <remarks>
/// <para>
/// <b>Why this exists rather than the platform's <c>IBindableFromHttpContext&lt;T&gt;</c>.</b>
/// Measured on 2026-08-30, and it invalidated the first design: the Request Delegate
/// Generator and this generator run in the <b>same</b> generation pass, over the same
/// input compilation, so RDG never sees a partial another generator added. A route
/// written as <c>MapPost("/x", (MyForm f) =&gt; …)</c> therefore emits
/// <c>hasBindAsync: false</c> and falls back to treating the model as a <b>JSON
/// body</b> — which meant a form POST was answered <b>415 by routing itself</b>,
/// before any code in this library ran.
/// </para>
/// <para>
/// This is §14's rule arriving from the other direction. There it was "a generator
/// cannot emit a <c>Map*</c> call"; here it is "a generator cannot give a type a
/// binder RDG will honour". The fix in both cases is the same: the route becomes a
/// delegate that takes only <see cref="HttpContext"/>, so nothing is inferred, and
/// the binding is an ordinary static call —
/// <see cref="FormEndpointRouteBuilderExtensions.MapFormPost{TForm}"/>.
/// </para>
/// <para>
/// A static abstract member, so the call is resolved at compile time against a
/// concrete <typeparamref name="TSelf"/>. No reflection, nothing for the trimmer to
/// lose, and the AOT publish stays clean.
/// </para>
/// </remarks>
public interface IFormModel<TSelf>
    where TSelf : class, IFormModel<TSelf>
{
    /// <summary>
    /// Reads the declared fields from the request's form.
    /// </summary>
    /// <param name="context">The request being bound.</param>
    /// <returns>The bound model. Never null: a form with no matching fields binds
    /// every member to <see langword="null"/> rather than failing.</returns>
    /// <exception cref="BadHttpRequestException">
    /// The request carries no form body. Thrown at 415 by
    /// <see cref="FormBinding.RequireFormContentType"/>, which the generated
    /// implementation calls first.
    /// </exception>
    static abstract ValueTask<TSelf> BindFormAsync(HttpContext context);

    /// <summary>
    /// The largest request body this model can bind, in bytes, or <c>0</c> when the
    /// model declares no file field and the host's own limit is the right one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the model has to publish this rather than raise the limit itself.</b>
    /// The binder runs after routing, after <c>UseAntiforgery()</c>, and after the
    /// body has been read — by which point
    /// <c>IHttpMaxRequestBodySizeFeature.IsReadOnly</c> is true and the host-wide
    /// ceiling has already refused the request. Measured 2026-09-04: a 300 KB upload
    /// to a field declaring <c>MaxBytes = 1_048_576</c> was refused by
    /// <c>NativeWebOptions.MaxRequestBodyBytes</c>'s 128 KiB
    /// default, so the declared ceiling was unreachable in the configuration the
    /// deployment runs.
    /// </para>
    /// <para>
    /// Read by <see cref="FormEndpointRouteBuilderExtensions.MapForm{TForm}"/>, which
    /// turns it into <c>IRequestSizeLimitMetadata</c> on the endpoint. Routing's own
    /// size-limit middleware applies that immediately after matching and therefore
    /// before anything reads the body, which is the only seam where the feature is
    /// still writable.
    /// </para>
    /// </remarks>
    static abstract long MaxBodyBytes { get; }
}
