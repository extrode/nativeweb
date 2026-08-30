using System.Diagnostics;
using System.Text;

using OpenTelemetry;

namespace __PRODUCT__;

/// <summary>
/// Rewrites the <c>url.query</c> span attribute so nothing survives but the names of parameters
/// that had a value, whatever the instrumentation put there.
/// </summary>
/// <remarks>
/// <para>
/// OpenTelemetry.Instrumentation.AspNetCore already redacts query values by default, and as of
/// 1.17.0 it does. This exists because that is a default and not a guarantee: the package reads
/// <c>OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION</c> (verified present in the
/// 1.17.0 assembly) and its matching option is <c>internal</c>, so an operator can switch
/// redaction off on the host and the app has no in-code way to say no. A reset token, a
/// <c>returnUrl</c> or any opaque handle travels as a query parameter, and "none of those ever
/// leaves the process" is worth holding in code that a host environment cannot reach past.
/// </para>
/// <para>
/// Names are kept rather than dropping the attribute wholesale, because they are most of its
/// value and none of its risk: which parameter a request carried is the first thing anyone
/// debugging an integration wants, and a name says nothing a route template does not.
/// </para>
/// <para>Skeleton copy -- the product owns this file after stamping.</para>
/// </remarks>
internal sealed class QueryRedactingProcessor : BaseProcessor<Activity>
{
    internal const string RedactedValue = "Redacted";

    public override void OnEnd(Activity data)
    {
        if (data.GetTagItem("url.query") is string query && query.Length > 0)
        {
            data.SetTag("url.query", Redact(query));
        }
    }

    /// <summary>
    /// <c>?code=abc&amp;state=xyz</c> becomes <c>?code=Redacted&amp;state=Redacted</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately does not decode anything. A percent-encoded name stays percent-encoded, so
    /// this cannot turn an encoded value into a decoded one, and a malformed query cannot make
    /// it throw. A parameter whose name is empty (<c>?=secret</c>) still gets its value replaced,
    /// which is the case a "split on = and trust the left side" implementation gets wrong.
    /// <para>
    /// A segment with no <c>=</c> at all is replaced wholesale rather than kept as a flag name.
    /// Keeping it was the first instinct — a flag has no value to leak — but it is only a flag
    /// if the caller meant it as one, and <c>?&lt;opaque-token&gt;</c> is a shape a client can
    /// send. Since nothing here can tell a flag from a bare token, the invariant is kept
    /// absolute: no segment of a query string survives except a name that is followed by
    /// <c>=</c>. A product that does use valueless query parameters and wants them legible in a
    /// trace changes that here, knowing what it is trading.
    /// </para>
    /// </remarks>
    internal static string Redact(string query)
    {
        ReadOnlySpan<char> remaining = query;
        var result = new StringBuilder(query.Length);

        if (remaining.Length > 0 && remaining[0] == '?')
        {
            result.Append('?');
            remaining = remaining[1..];
        }

        bool first = true;
        foreach (Range range in remaining.Split('&'))
        {
            if (!first)
            {
                result.Append('&');
            }

            first = false;

            ReadOnlySpan<char> pair = remaining[range];
            int equals = pair.IndexOf('=');
            if (equals < 0)
            {
                if (pair.Length > 0)
                {
                    result.Append(RedactedValue);
                }
            }
            else
            {
                result.Append(pair[..(equals + 1)]).Append(RedactedValue);
            }
        }

        return result.ToString();
    }
}
