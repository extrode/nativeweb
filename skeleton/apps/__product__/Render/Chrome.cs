using Microsoft.AspNetCore.Antiforgery;

using __PRODUCT__.Middleware;

using NativeWeb.Render;
using NativeWeb.Security;

namespace __PRODUCT__.Render;

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
    // GetAndStoreTokens is idempotent per request, so the layout's <meta> token
    // and every form's hidden input come from the same pair.
    public static Chrome For(HttpContext context, AssetUrls assets, IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        return new(
            Locales.Get(context.GetLocale()),
            assets,
            (string)context.Items[SecurityHeadersMiddleware.ItemKey]!,
            tokens.FormFieldName,
            tokens.RequestToken!,
            BotTraps.IssueCanary(context));
    }
}
