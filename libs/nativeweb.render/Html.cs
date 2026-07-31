namespace NativeWeb.Render;

/// <summary>
/// Pre-rendered markup that a template may emit without HTML encoding. This type is
/// the only unencoded path for VALUES through <see cref="TemplateWriter"/> (spec 003
/// R11): a plain string always encodes. Auditing raw output means grepping for
/// <c>Html.Raw</c> plus <c>WriteRaw</c> (generated static template chunks).
/// </summary>
public readonly struct Html
{
    internal readonly string? Markup;

    private Html(string markup) => Markup = markup;

    /// <summary>
    /// Marks <paramref name="markup"/> as safe, already-encoded HTML. The caller owns
    /// that claim — never pass user input through this without encoding it first.
    /// </summary>
    public static Html Raw(string markup) => new(markup);

    /// <summary>Renders nothing.</summary>
    public static Html Empty => default;
}
