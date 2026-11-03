namespace NativeWeb.Web;

public enum FlashKind
{
    Success,
    Error,
}

// One-shot notice across a post-action navigation (spec 005 R4). An endpoint
// sets it immediately before the success redirect; the next full page render
// takes it and shows the standard banner, exactly once, on both transports —
// the no-JS path follows the real redirect and the enhanced path follows
// HX-Redirect with a full window.location navigation, so either way the next
// request renders the full layout. A cookie rather than any server-side
// state: the __Host- prefix pins it Secure/Path=/ with no Domain so it can't
// be planted from a subdomain. Self-expires quickly in case the follow-up
// render never happens.
//
// Take returns an (Ok, Error) pair of empty strings feeding the layout's
// zero-length call-site guards, the shape template-rendered apps use.
//
// Set has no caller here yet by design: the skeleton ships the slot wired
// (layout params + read-once Take at every full-page render site) so a
// product's first success redirect only needs the Set call. Every full-page
// render site a product adds must Take — the layout's required FlashOk /
// FlashError params make the compiler point at any site that forgets.
public static class Flash
{
    // internal, not private: PublicPageCache tests this cookie's presence to
    // decide whether a response can be shared (spec 006).
    public const string CookieName = "__Host-nw-flash";
    private const string SuccessPrefix = "ok:";
    private const string ErrorPrefix = "err:";
    private const int MaxMessageLength = 300;

    public static void Set(HttpContext context, FlashKind kind, string message)
    {
        if (message.Length > MaxMessageLength)
        {
            // Back off one when the cut would split a surrogate pair — a lone
            // surrogate makes Uri.EscapeDataString throw.
            int cut = char.IsHighSurrogate(message[MaxMessageLength - 1]) ? MaxMessageLength - 1 : MaxMessageLength;
            message = message[..cut];
        }

        string prefix = kind == FlashKind.Success ? SuccessPrefix : ErrorPrefix;
        context.Response.Cookies.Append(CookieName, prefix + Uri.EscapeDataString(message), new CookieOptions
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(5),
        });
    }

    // Read-once: taking the flash expires the cookie in the same response, so
    // a reload of the flashed page shows nothing. Returns empty strings when
    // there is no flash or the value doesn't parse (a foreign or truncated
    // cookie is dropped, never rendered) — empty feeds the layout's
    // zero-length guard directly (the {{if}} lives at the call site in
    // layout/page.html, NOT in the banner partials: a partial whose body is a
    // bare else-less {{if}} is an unclean splice that bails the build-time
    // HTML formatter for every page).
    public static (string Ok, string Error) Take(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out string? raw) || raw is null)
        {
            return ("", "");
        }

        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });

        bool success = raw.StartsWith(SuccessPrefix, StringComparison.Ordinal);
        if (!success && !raw.StartsWith(ErrorPrefix, StringComparison.Ordinal))
        {
            return ("", "");
        }

        string message;
        try
        {
            message = Uri.UnescapeDataString(raw[(success ? SuccessPrefix : ErrorPrefix).Length..]);
        }
        catch (UriFormatException)
        {
            return ("", "");
        }

        if (message.Length is 0 or > MaxMessageLength)
        {
            return ("", "");
        }

        return success ? (message, "") : ("", message);
    }
}
