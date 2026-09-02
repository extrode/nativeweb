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
/// offer. A header that does not parse counts as absent. An offer whose most
/// specific matching range says <c>q=0</c> is refused even when a wildcard would
/// admit it. When a range matches several offers, the earliest offer wins, which
/// is what makes <c>*/*</c> select the default.
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

        // Each offer's quality is that of the most specific range matching it,
        // which is how RFC 9110 reads an Accept header: "*/*, text/html;q=0"
        // refuses text/html and welcomes everything else. Among the offers with
        // the highest quality, the one whose deciding range is more specific
        // wins, then the one whose range came first in the header, then the
        // earlier offer.
        Format? best = null;
        (double Quality, int Specificity, int Index) bestRank = (0.0, -1, int.MaxValue);

        foreach (Format offer in offers)
        {
            if (Rank(candidates, offer) is not { Quality: > 0.0 } rank)
            {
                continue;
            }

            bool better = best is null
                || rank.Quality > bestRank.Quality
                || (rank.Quality == bestRank.Quality
                    && (rank.Specificity > bestRank.Specificity
                        || (rank.Specificity == bestRank.Specificity && rank.Index < bestRank.Index)));

            if (better)
            {
                best = offer;
                bestRank = rank;
            }
        }

        return best is { } chosen
            ? new Negotiated(NegotiationOutcome.Chosen, chosen, ByAccept: true)
            : new Negotiated(NegotiationOutcome.NotAcceptable, fallback, ByAccept: true);
    }

    /// <summary>
    /// Whether a response's <c>Content-Type</c> is <paramref name="format"/>'s media
    /// type, parameters aside: <c>application/json; charset=utf-8</c> is, and
    /// <c>application/jsonl</c> is not.
    /// </summary>
    /// <param name="contentType">The response's Content-Type header, or null.</param>
    /// <param name="format">The format the response was meant to answer.</param>
    /// <returns>True when the media type is the format's.</returns>
    public static bool ContentTypeIs(string? contentType, Format format)
    {
        if (contentType is null)
        {
            return false;
        }

        ReadOnlySpan<char> mediaType = contentType.AsSpan();
        int end = mediaType.IndexOf(';');
        mediaType = (end < 0 ? mediaType : mediaType[..end]).Trim();

        return mediaType.Equals(format.MediaType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The 404 or 406 body: the offered media types, one line.</summary>
    /// <param name="offers">The offered formats.</param>
    /// <returns><c>Offered: text/html, application/json</c> and a newline.</returns>
    public static string OfferedLine(IEnumerable<Format> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);

        return "Offered: " + string.Join(", ", offers.Select(static o => o.MediaType)) + "\n";
    }

    // The most specific range matching the offer decides its quality; among
    // equally specific ranges the first in the header does. Null when nothing
    // matches at all.
    private static (double Quality, int Specificity, int Index)? Rank(IList<MediaTypeHeaderValue> candidates, Format offer)
    {
        (double Quality, int Specificity, int Index)? rank = null;

        for (int index = 0; index < candidates.Count; index++)
        {
            MediaTypeHeaderValue candidate = candidates[index];

            if (!Matches(candidate, offer))
            {
                continue;
            }

            int specificity = Specificity(candidate);

            if (rank is null || specificity > rank.Value.Specificity)
            {
                rank = (candidate.Quality ?? 1.0, specificity, index);
            }
        }

        return rank;
    }

    private static int Specificity(MediaTypeHeaderValue candidate) =>
        candidate.MatchesAllTypes ? 0
        : candidate.MatchesAllSubTypes ? 1
        : 2;

    // A media type without a slash is a caller's mistake in a hand-built Format;
    // it matches nothing but */* rather than throwing inside every request.
    private static bool Matches(MediaTypeHeaderValue candidate, Format offer)
    {
        if (candidate.MatchesAllTypes)
        {
            return true;
        }

        int slash = offer.MediaType.IndexOf('/', StringComparison.Ordinal);

        if (slash < 0)
        {
            return false;
        }

        ReadOnlySpan<char> type = offer.MediaType.AsSpan(0, slash);
        ReadOnlySpan<char> subtype = offer.MediaType.AsSpan(slash + 1);

        return candidate.Type.AsSpan().Equals(type, StringComparison.OrdinalIgnoreCase)
            && (candidate.MatchesAllSubTypes
                || candidate.SubType.AsSpan().Equals(subtype, StringComparison.OrdinalIgnoreCase));
    }
}
