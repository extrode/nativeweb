using Microsoft.AspNetCore.Http;

using NativeWeb.Render;
using NativeWeb.Render.Tests.Unit.Templates;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// R14–R16: build-time dialect fallthrough, generated Locales class, the
/// implicit template locale parameter, and request-locale negotiation.
/// </summary>
public sealed class LocalizationTests
{
    [Fact]
    public void En_IsTheRootCatalog()
    {
        Assert.Equal("Bookmarks", Locales.En["app.title"]);
        Assert.Equal("Hello", Locales.En["app.greeting"]);
        Assert.Equal("ltr", Locales.En.Dir);
        Assert.Equal("en", Locales.En.Lang);
    }

    [Fact]
    public void Dialect_OverridesOnlyItsOwnKeys_AndInheritsTheRest()
    {
        Assert.Equal("Bookmarks (US)", Locales.EnUs["app.title"]);
        Assert.Equal("Hello", Locales.EnUs["app.greeting"]);
        Assert.Equal("en-US", Locales.EnUs.Lang);
    }

    [Fact]
    public void Language_FallsThroughToEnglishPerKey()
    {
        Assert.Equal("Selamat datang", Locales.Ms["app.greeting"]);
        Assert.Equal("Bookmarks", Locales.Ms["app.title"]);
        Assert.Equal("Home", Locales.Ms["nav.home"]);
    }

    [Fact]
    public void RtlLanguage_GetsRtlDirection()
    {
        Assert.Equal("rtl", Locales.Ar.Dir);
        Assert.Equal("إشارات", Locales.Ar["app.title"]);
    }

    [Fact]
    public void Get_ResolvesKnownIds_AndFallsBackToEnglish()
    {
        Assert.Same(Locales.EnUs, Locales.Get("en-us"));
        Assert.Same(Locales.Ms, Locales.Get("ms"));
        Assert.Same(Locales.En, Locales.Get("xx"));
        Assert.Same(Locales.En, Locales.Get(null));
        Assert.Equal(["ar", "en", "en-us", "ms"], Locales.All);
    }

    [Fact]
    public void Format_SubstitutesNamedPlaceholders()
    {
        Assert.Equal("Sign in to epass", Locales.En.Format("emails.subject", ("product", "epass")));
    }

    [Fact]
    public void Format_DoesNotResubstitutePlaceholdersInValues()
    {
        // "Sign in to {product}" with a product that itself contains "{product}":
        // a naive sequential Replace would loop the value back through.
        Assert.Equal(
            "Sign in to see {product}",
            Locales.En.Format("emails.subject", ("product", "see {product}")));
    }

    [Fact]
    public void Format_LeavesUnmatchedPlaceholdersInPlace()
    {
        Assert.Equal("Sign in to {product}", Locales.En.Format("emails.subject"));
    }

    [Fact]
    public void UnknownKey_RendersTheKeyItself()
    {
        Assert.Equal("no.such.key", Locales.En["no.such.key"]);
    }

    [Fact]
    public void LocalizedTemplate_TakesLocaleAndRendersIt()
    {
        string html = Localized.ToHtmlString(Locales.Ms, Name: "Aiman");

        Assert.Contains("<h1>Bookmarks</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Selamat datang, Aiman", html, StringComparison.Ordinal);
        // The partial received the locale transitively.
        Assert.Contains("<span>Home</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizedTemplate_EncodesCatalogValues()
    {
        // Catalog strings go through Write(string), so markup in a translation
        // cannot break the page.
        string html = Localized.ToHtmlString(Locales.En, Name: "<b>x</b>");

        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_PrefersExactDialect_ThenBaseLanguage()
    {
        string[] supported = ["en", "en-us", "ms"];

        Assert.Equal("en-us", LocaleNegotiation.Match("en-US", supported));
        Assert.Equal("en", LocaleNegotiation.Match("en-GB", supported));
        Assert.Equal("ms", LocaleNegotiation.Match("ms-MY", supported));
        Assert.Null(LocaleNegotiation.Match("ar", supported));
        Assert.Null(LocaleNegotiation.Match("", supported));
    }

    [Fact]
    public void AcceptLanguageOrder_SortsByQuality_AndSkipsWildcard()
    {
        string[] order = [.. LocaleNegotiation.AcceptLanguageOrder("ms;q=0.8, en-US, *;q=0.1, ar;q=0")];

        Assert.Equal(["en-US", "ms"], order);
    }

    [Fact]
    public void AcceptLanguageOrder_RejectsNonRfcQValues()
    {
        // RFC 9110 qvalue is a plain decimal in [0, 1]: exponents and
        // out-of-range values fall back to the default quality of 1.
        string[] order = [.. LocaleNegotiation.AcceptLanguageOrder("ms;q=1e9, en;q=0.5, ar")];

        Assert.Equal(["ms", "ar", "en"], order);
    }

    [Fact]
    public async Task Negotiate_UserPreference_WinsOverHeader()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.AcceptLanguage = "ms";
        LocaleNegotiationOptions options = new()
        {
            SupportedLocales = ["en", "ms"],
            UserPreference = _ => ValueTask.FromResult<string?>("en"),
        };

        Assert.Equal("en", await LocaleNegotiation.NegotiateAsync(context, options));
    }

    [Fact]
    public async Task Negotiate_Cookie_BeatsHeader_HeaderBeatsDefault()
    {
        DefaultHttpContext withCookie = new();
        withCookie.Request.Headers.Cookie = "locale=ms";
        withCookie.Request.Headers.AcceptLanguage = "en";

        DefaultHttpContext headerOnly = new();
        headerOnly.Request.Headers.AcceptLanguage = "ms-MY, en;q=0.5";

        DefaultHttpContext bare = new();

        LocaleNegotiationOptions options = new() { SupportedLocales = ["en", "ms"] };

        Assert.Equal("ms", await LocaleNegotiation.NegotiateAsync(withCookie, options));
        Assert.Equal("ms", await LocaleNegotiation.NegotiateAsync(headerOnly, options));
        Assert.Equal("en", await LocaleNegotiation.NegotiateAsync(bare, options));
    }
}
