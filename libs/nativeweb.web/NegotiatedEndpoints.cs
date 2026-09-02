using Microsoft.AspNetCore.Builder;
using Microsoft.Net.Http.Headers;

using NativeWeb.Render;

namespace NativeWeb.Web;

/// <summary>
/// The two routes one <see cref="NegotiatedEndpoints.MapNegotiated"/> call maps,
/// as one builder: a convention added here lands on both, so
/// <c>.CacheOutput(...)</c>, <c>.RequireRateLimiting(...)</c> and
/// <c>.AsPublicPage(...)</c> read the same as on any route.
/// </summary>
public sealed class NegotiatedRouteBuilder : IEndpointConventionBuilder
{
    private readonly IEndpointConventionBuilder _plain;
    private readonly IEndpointConventionBuilder _extension;

    internal NegotiatedRouteBuilder(IEndpointConventionBuilder plain, IEndpointConventionBuilder extension)
    {
        _plain = plain;
        _extension = extension;
    }

    /// <inheritdoc />
    public void Add(Action<EndpointBuilder> convention)
    {
        _plain.Add(convention);
        _extension.Add(convention);
    }

    /// <inheritdoc />
    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        _plain.Finally(finallyConvention);
        _extension.Finally(finallyConvention);
    }
}

/// <summary>
/// One route, several representations (spec 021 R5 to R8).
/// </summary>
public static class NegotiatedEndpoints
{
    /// <summary>
    /// Maps <paramref name="pattern"/> and <c>pattern.{format}</c> to one handler
    /// that is told which of <paramref name="offers"/> the request asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The choice is made before the handler runs, from the offers declared here:
    /// an extension the route does not offer is a 404 and an Accept header that
    /// admits nothing is a 406, both answered as <c>text/plain</c> naming the
    /// offered media types and neither reaching the handler or its data. The
    /// extensionless route says <c>Vary: Accept</c>; the extension route does not.
    /// The chosen format is in <see cref="HttpContext.Items"/> under
    /// <see cref="Negotiation.ItemKey"/>.
    /// </para>
    /// <para>
    /// Both routes are mapped with a <see cref="RequestDelegate"/>, not with
    /// <paramref name="handler"/> itself: handed a lambda with a <see cref="Format"/>
    /// parameter, the Request Delegate Generator would try to bind it from the
    /// request and fail at run time. Nothing here is bound by the generator at all.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route, <c>/articles</c>; no trailing slash.</param>
    /// <param name="offers">The formats offered, the default first; at least one, no repeats.</param>
    /// <param name="handler">Produces the representation it is asked for.</param>
    /// <returns>A builder whose conventions apply to both routes.</returns>
    public static NegotiatedRouteBuilder MapNegotiated(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        IReadOnlyList<Format> offers,
        Func<HttpContext, Format, ValueTask<IResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfZero(offers.Count);

        if (!pattern.StartsWith('/') || pattern.EndsWith('/') || pattern.Contains('{', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A negotiated pattern is a plain path starting with '/' and not ending in one; got '{pattern}'.",
                nameof(pattern));
        }

        if (offers.Distinct().Count() != offers.Count)
        {
            throw new ArgumentException("A format is offered at most once.", nameof(offers));
        }

        Format[] offered = [.. offers];
        NegotiationMetadata metadata = new(offered);
        string refusal = Negotiation.OfferedLine(offered);

        async Task Handle(HttpContext http)
        {
            Negotiated choice = Negotiation.Choose(http.Request, offered);

            if (choice.ByAccept)
            {
                http.Response.Headers.Append(HeaderNames.Vary, "Accept");
            }

            IResult result = choice.Outcome switch
            {
                NegotiationOutcome.NotFound => FormatResults.Text(refusal, StatusCodes.Status404NotFound),
                NegotiationOutcome.NotAcceptable => FormatResults.Text(refusal, StatusCodes.Status406NotAcceptable),
                _ => await Chosen(http, choice.Format).ConfigureAwait(false),
            };

            await result.ExecuteAsync(http).ConfigureAwait(false);
        }

        async ValueTask<IResult> Chosen(HttpContext http, Format format)
        {
            http.Items[Negotiation.ItemKey] = format;
            return await handler(http, format).ConfigureAwait(false);
        }

        IEndpointConventionBuilder plain = endpoints.MapGet(pattern, (RequestDelegate)Handle).WithMetadata(metadata);
        IEndpointConventionBuilder extension = endpoints
            .MapGet(pattern + ".{" + Negotiation.RouteValue + "}", (RequestDelegate)Handle)
            .WithMetadata(metadata);

        return new NegotiatedRouteBuilder(plain, extension);
    }
}
