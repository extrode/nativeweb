using Microsoft.AspNetCore.Antiforgery;


using NativeWeb.Render;
using NativeWeb.Security;

namespace NativeWeb.Web;

/// <summary>
/// Everything the layout template needs on every page, gathered once per
/// render. Values are raw (unencoded); the template writer encodes on output.
/// The skeleton has no accounts, so nothing here describes a signed-in user;
/// a product that grows sign-in adds those fields alongside these.
/// </summary>
public readonly record struct Chrome(
    LocaleCatalog Locale,
    AssetUrls Assets,
    string Nonce,
    string AntiforgeryFieldName,
    string AntiforgeryToken,
    string BotCanary)
{
    // The chrome for a page that carries a form, an identity, or both. Every
    // page took this path until spec 006; the cacheable public pages take
    // Anonymous instead. GetAndStoreTokens is idempotent per request, so the
    // layout's <meta> token and every form's hidden input come from the same
    // pair.
    //
    // The catalog is a parameter and not a lookup. Both factories read
    // Locales.Get(context.GetLocale()) while this file was template text, and
    // Locales is emitted per product by the render generator from that product's
    // own locales/*.json — so the call compiled only inside a product and this
    // file could not move into the framework with it (spec 015, found by T1).
    // The caller passes the catalog and keeps the one line that names its own
    // generated type.
    public static Chrome For(
        HttpContext context, AssetUrls assets, IAntiforgery antiforgery, LocaleCatalog locale)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        return new(
            locale,
            assets,
            (string)context.Items[SecurityHeadersMiddleware.ItemKey]!,
            tokens.FormFieldName,
            tokens.RequestToken!,
            BotTraps.IssueCanary(context));
    }

    /// <summary>
    /// The chrome for an anonymous public page (spec 006 R1). Every field that
    /// would differ between two otherwise-identical responses is empty, so the
    /// render is byte-stable per (path, page, locale) and can be shared from
    /// the output cache:
    ///
    /// <list type="bullet">
    /// <item>no <see cref="IAntiforgery.GetAndStoreTokens"/> — it would mint a
    /// fresh masked request token AND set the antiforgery cookie, and a
    /// Set-Cookie is both a per-response byte and a response OutputCache
    /// refuses to store;</item>
    /// <item>no nonce — the response is authorized by the build-generated
    /// script hashes in <see cref="PublicPageCache.Csp"/> instead;</item>
    /// <item>no canary — <see cref="BotTraps.IssueCanary"/> signs the render
    /// instant, so it differs on every call. A public page renders no trap
    /// fields, so this drops a cost rather than a defence.</item>
    /// </list>
    ///
    /// A product that grows sign-in adds its SignedIn/UserName fields as
    /// <b>constant</b> here: the caller reaches this factory only for a request
    /// that carried no session cookie, and passing an identity through it would
    /// leak that user's name into a shared entry.
    /// </summary>
    public static Chrome Anonymous(HttpContext context, AssetUrls assets, LocaleCatalog locale) =>
        new(locale, assets, "", "", "", "");
}
