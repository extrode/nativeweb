using NativeWeb.Forms;

namespace NativeWeb.Sample;

/// <summary>
/// The filter posted by the feed's search box, declared rather than read.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what a consumer writes to get declarative form binding
/// (spec 014). The binder, its 415 guard and its refusal to read anything not named
/// here are emitted by <c>NativeWeb.Forms.Generator</c> onto this same partial.
/// </para>
/// <para>
/// <b>Both members are <c>string?</c>, and that is not laziness.</b> Every value in
/// a form arrives as text, and a field the client omits arrives not at all — a
/// declared <c>int</c> would have to invent a number for "the user cleared the box".
/// The conversion happens in the handler, where a bad one can be reported to the
/// person who typed it.
/// </para>
/// <para>
/// It also carries this app's AOT proof. §14 measured that a generator's output is
/// invisible to <c>dotnet build</c>'s AOT analysis when the route is emitted too;
/// here the route is hand-written in this assembly, so <c>dotnet publish -c Release
/// -r &lt;rid&gt;</c> resolves the emitted <c>BindAsync</c> statically and reports
/// no IL2026 or IL3050.
/// </para>
/// </remarks>
/// <param name="Query">Free text to match against a title, or null for everything.</param>
/// <param name="Take">How many to show. Named <c>max</c> on the wire.</param>
[Form]
public sealed partial record ArticleFilter(
    string? Query,
    [FormField("max")] string? Take);
