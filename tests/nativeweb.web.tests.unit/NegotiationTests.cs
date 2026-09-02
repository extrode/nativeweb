using Microsoft.AspNetCore.Http;

using NativeWeb.Render;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

public sealed class NegotiationTests
{
    private static readonly Format[] HtmlJsonCsv = [Format.Html, Format.Json, Format.Csv];

    private static HttpRequest Request(string? accept = null, string? extension = null)
    {
        DefaultHttpContext http = new();

        if (accept is not null)
        {
            http.Request.Headers.Accept = accept;
        }

        if (extension is not null)
        {
            http.Request.RouteValues[Negotiation.RouteValue] = extension;
        }

        return http.Request;
    }

    [Theory]
    [InlineData(null, "text/html", true)]
    [InlineData("", "text/html", true)]
    [InlineData("*/*", "text/html", true)]
    [InlineData("text/*", "text/html", true)]
    [InlineData("application/*", "application/json", true)]
    [InlineData("application/json", "application/json", true)]
    [InlineData("APPLICATION/JSON", "application/json", true)]
    [InlineData("text/html;q=0.5, application/json;q=0.9", "application/json", true)]
    [InlineData("application/json;q=0.9, text/html;q=0.5", "application/json", true)]
    [InlineData("text/html;q=0.9, application/json;q=0.9", "text/html", true)]
    [InlineData("*/*;q=0.9, text/csv", "text/csv", true)]
    [InlineData("text/csv;q=0, */*", "text/html", true)]
    [InlineData("text/*, application/json", "application/json", true)]
    [InlineData("image/png, */*;q=0.1", "text/html", true)]
    [InlineData("text/html, application/xhtml+xml, application/xml;q=0.9, */*;q=0.8", "text/html", true)]
    [InlineData("this is not a media type", "text/html", true)]
    [InlineData("*/*, text/html;q=0", "application/json", true)]
    [InlineData("text/*, text/html;q=0.1", "text/csv", true)]
    [InlineData("text/*;q=0.2, text/html;q=0.9, application/json;q=0.5", "text/html", true)]
    [InlineData("text/*;q=0, application/json", "application/json", true)]
    public void Choose_FollowsAcceptByQualityThenSpecificityThenOrder(string? accept, string expected, bool byAccept)
    {
        Negotiated choice = Negotiation.Choose(Request(accept), HtmlJsonCsv);

        Assert.Equal(NegotiationOutcome.Chosen, choice.Outcome);
        Assert.Equal(expected, choice.Format.MediaType);
        Assert.Equal(byAccept, choice.ByAccept);
    }

    [Theory]
    [InlineData("json", "application/json")]
    [InlineData("csv", "text/csv")]
    [InlineData("html", "text/html")]
    public void Choose_TakesTheExtensionOverAnyAcceptHeader(string extension, string expected)
    {
        Negotiated choice = Negotiation.Choose(Request("text/html", extension), HtmlJsonCsv);

        Assert.Equal(NegotiationOutcome.Chosen, choice.Outcome);
        Assert.Equal(expected, choice.Format.MediaType);
        Assert.False(choice.ByAccept);
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("atom")]
    [InlineData("exe")]
    [InlineData("a.b")]
    public void Choose_RefusesAnExtensionTheRouteDoesNotOffer(string extension)
    {
        Negotiated choice = Negotiation.Choose(Request("application/json", extension), HtmlJsonCsv);

        Assert.Equal(NegotiationOutcome.NotFound, choice.Outcome);
        Assert.False(choice.ByAccept);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/xml, image/*")]
    [InlineData("text/html;q=0")]
    [InlineData("text/html;q=0, */*;q=0")]
    [InlineData("text/*;q=0, application/*;q=0, */*")]
    public void Choose_RefusesAnAcceptHeaderThatAdmitsNothing(string accept)
    {
        Negotiated choice = Negotiation.Choose(Request(accept), HtmlJsonCsv);

        Assert.Equal(NegotiationOutcome.NotAcceptable, choice.Outcome);
        Assert.True(choice.ByAccept);
    }

    [Fact]
    public void Choose_DefaultsToTheFirstOfferWhateverItIs()
    {
        Negotiated choice = Negotiation.Choose(Request(), [Format.Atom, Format.Rss]);

        Assert.Equal(NegotiationOutcome.Chosen, choice.Outcome);
        Assert.Equal(Format.Atom, choice.Format);
    }

    [Fact]
    public void Choose_TreatsAHandBuiltMediaTypeWithoutASlashAsMatchingOnlyTheWildcard()
    {
        Format[] offers = [new Format("bin", "octets", IsText: false), Format.Json];

        Assert.Equal(Format.Json, Negotiation.Choose(Request("application/json, octets"), offers).Format);
        Assert.Equal(offers[0], Negotiation.Choose(Request("*/*"), offers).Format);
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("Application/JSON;charset=utf-8", true)]
    [InlineData("application/jsonl", false)]
    [InlineData("application/json+ld", false)]
    [InlineData("text/plain", false)]
    [InlineData(null, false)]
    public void ContentTypeIs_ComparesTheMediaTypeAlone(string? contentType, bool expected)
    {
        Assert.Equal(expected, Negotiation.ContentTypeIs(contentType, Format.Json));
    }

    [Fact]
    public void Choose_NeedsAtLeastOneOffer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Negotiation.Choose(Request(), []));
    }

    [Fact]
    public void OfferedLine_NamesEveryMediaTypeInOrder()
    {
        Assert.Equal("Offered: text/html, application/json, text/csv\n", Negotiation.OfferedLine(HtmlJsonCsv));
    }
}
