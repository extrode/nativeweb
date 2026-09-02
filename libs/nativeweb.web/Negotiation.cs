using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

using NativeWeb.Render;

namespace NativeWeb.Web;

/// <summary>How a negotiated request was resolved.</summary>
public enum NegotiationOutcome
{
    /// <summary>A representation was chosen.</summary>
    Chosen,

    /// <summary>The path named an extension the route does not offer: 404.</summary>
    NotFound,

    /// <summary>The Accept header admits none of the offers and no wildcard: 406.</summary>
    NotAcceptable,
}

/// <summary>The result of <see cref="Negotiation.Choose"/>.</summary>
/// <param name="Outcome">Chosen, or which refusal.</param>
/// <param name="Format">The chosen format; the route's default when the outcome is a refusal.</param>
/// <param name="ByAccept">
/// Whether the choice depended on the Accept header rather than on an extension, which is
/// what decides whether the response says <c>Vary: Accept</c>. True whenever no extension
/// was given, because the same URL then answers differently to different headers, wildcard
/// and absent header included.
/// </param>
public readonly record struct Negotiated(NegotiationOutcome Outcome, Format Format, bool ByAccept);

/// <summary>
/// Endpoint metadata carrying what a negotiated route offers, so the output-cache
/// policy can make the same choice the endpoint makes.
/// </summary>
/// <param name="offers">The offered formats, the default first.</param>
public sealed class NegotiationMetadata(IReadOnlyList<Format> offers)
{
    /// <summary>The offered formats, the default first.</summary>
    public IReadOnlyList<Format> Offers { get; } = offers;
}

/// <summary>
/// The one chooser behind <c>MapNegotiated</c> and <see cref="PublicPageCachePolicy"/>
/// (spec 021 R5, R6). Pure: it reads the route value and the Accept header and nothing
/// else, so both callers get the same answer for the same request.
/// </summary>
/// <remarks>
/// Precedence: an extension on the path, then the Accept header by quality and then
/// specificity (<c>type/sub</c> over <c>type/*</c> over <c>*/*</c>), then the first
/// offer. A header that does not parse counts as absent. A candidate with
/// <c>q=0</c> is excluded. When a candidate matches several offers, the earliest
/// offer wins, which is what makes <c>*/*</c> select the default.
/// </remarks>
public static class Negotiation
{
    /// <summary>The route value the extension route binds: <c>{format}</c>.</summary>
    public const string RouteValue = "format";

    /// <summary>The <see cref="HttpContext.Items"/> key holding the chosen <see cref="Format"/>.</summary>
    public const string ItemKey = "nativeweb.format";

    /// <summary>Chooses a representation for <paramref name="request"/> among <paramref name="offers"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="offers">The offered formats, the default first. Must not be empty.</param>
    /// <returns>The choice or the refusal.</returns>
    public static Negotiated Choose(HttpRequest request, IReadOnlyList<Format> offers)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentOutOfRangeException.ThrowIfZero(offers.Count);

        Format fallback = offers[0];

        if (request.RouteValues.TryGetValue(RouteValue, out object? raw) && raw is string extension && extension.Length > 0)
        {
            return Format.TryFromExtension(extension, out Format named) && offers.Contains(named)
                ? new Negotiated(NegotiationOutcome.Chosen, named, ByAccept: false)
                : new Negotiated(NegotiationOutcome.NotFound, fallback, ByAccept: false);
        }

        StringValues accept = request.Headers.Accept;

        if (StringValues.IsNullOrEmpty(accept)
            || !MediaTypeHeaderValue.TryParseList(accept, out IList<MediaTypeHeaderValue>? candidates)
            || candidates.Count == 0)
        {
            return new Negotiated(NegotiationOutcome.Chosen, fallback, ByAccept: true);
        }

        foreach (MediaTypeHeaderValue candidate in Ordered(candidates))
        {
            foreach (Format offer in offers)
            {
                if (Matches(candidate, offer))
                {
                    return new Negotiated(NegotiationOutcome.Chosen, offer, ByAccept: true);
                }
            }
        }

        return new Negotiated(NegotiationOutcome.NotAcceptable, fallback, ByAccept: true);
    }

    /// <summary>The 404 or 406 body: the offered media types, one line.</summary>
    /// <param name="offers">The offered formats.</param>
    /// <returns><c>Offered: text/html, application/json</c> and a newline.</returns>
    public static string OfferedLine(IEnumerable<Format> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);

        return "Offered: " + string.Join(", ", offers.Select(static o => o.MediaType)) + "\n";
    }

    // Quality first, then specificity, then the header's own order; q=0 dropped.
    private static IEnumerable<MediaTypeHeaderValue> Ordered(IList<MediaTypeHeaderValue> candidates) =>
        candidates
            .Select(static (candidate, index) => (Candidate: candidate, Index: index))
            .Where(static c => (c.Candidate.Quality ?? 1.0) > 0.0)
            .OrderByDescending(static c => c.Candidate.Quality ?? 1.0)
            .ThenByDescending(static c => Specificity(c.Candidate))
            .ThenBy(static c => c.Index)
            .Select(static c => c.Candidate);

    private static int Specificity(MediaTypeHeaderValue candidate) =>
        candidate.MatchesAllTypes ? 0
        : candidate.MatchesAllSubTypes ? 1
        : 2;

    private static bool Matches(MediaTypeHeaderValue candidate, Format offer)
    {
        int slash = offer.MediaType.IndexOf('/', StringComparison.Ordinal);
        ReadOnlySpan<char> type = offer.MediaType.AsSpan(0, slash);
        ReadOnlySpan<char> subtype = offer.MediaType.AsSpan(slash + 1);

        return candidate.MatchesAllTypes
            || (candidate.Type.AsSpan().Equals(type, StringComparison.OrdinalIgnoreCase)
                && (candidate.MatchesAllSubTypes
                    || candidate.SubType.AsSpan().Equals(subtype, StringComparison.OrdinalIgnoreCase)));
    }
}
