using System.Collections.Frozen;

namespace NativeWeb.Render;

/// <summary>
/// One locale's fully resolved string table (spec 003 R14–R15). Fallthrough is
/// resolved at BUILD time by the generator — a dialect table already contains its
/// base language's strings and every English root string — so lookup here is a
/// single frozen-dictionary probe with no chain walk.
/// </summary>
public sealed class LocaleCatalog
{
    private readonly FrozenDictionary<string, string> _strings;

    public LocaleCatalog(string locale, string dir, KeyValuePair<string, string>[] strings)
    {
        Locale = locale;
        Dir = dir;
        Lang = ToHtmlLang(locale);
        _strings = strings.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Lowercase locale id as authored, e.g. "en", "en-us", "ms".</summary>
    public string Locale { get; }

    /// <summary>BCP-47 form for the html lang attribute, e.g. "en-US".</summary>
    public string Lang { get; }

    /// <summary>Text direction for the html dir attribute: "ltr" or "rtl".</summary>
    public string Dir { get; }

    /// <summary>
    /// Looks up a catalog string. Template keys are compile-checked against the
    /// English root, so a miss can only come from a hand-written key; returning
    /// the key itself keeps that visible without throwing mid-render.
    /// </summary>
    public string this[string key] => _strings.GetValueOrDefault(key, key);

    /// <summary>
    /// Looks up a catalog string and substitutes <c>{name}</c> placeholders.
    /// Placeholders with no matching argument are left in place. A single
    /// left-to-right scan over the catalog string: placeholders occurring in a
    /// substituted VALUE are never re-substituted, so argument order cannot
    /// splice one argument into another.
    /// </summary>
    public string Format(string key, params ReadOnlySpan<(string Name, string? Value)> args)
    {
        string template = this[key];
        var result = new System.Text.StringBuilder(template.Length + 32);
        int position = 0;
        while (position < template.Length)
        {
            int open = template.IndexOf('{', position);
            if (open < 0)
            {
                result.Append(template, position, template.Length - position);
                break;
            }

            int close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(template, position, template.Length - position);
                break;
            }

            result.Append(template, position, open - position);
            ReadOnlySpan<char> name = template.AsSpan(open + 1, close - open - 1);
            bool substituted = false;
            foreach ((string argName, string? argValue) in args)
            {
                if (name.SequenceEqual(argName))
                {
                    result.Append(argValue);
                    substituted = true;
                    break;
                }
            }

            if (!substituted)
            {
                result.Append(template, open, close - open + 1);
            }

            position = close + 1;
        }

        return result.ToString();
    }

    // Two-subtag ids only ("en-us" -> "en-US"): a three-subtag id like
    // "zh-hans-cn" would need script-aware casing, which no shipped locale has.
    private static string ToHtmlLang(string locale)
    {
        int dash = locale.IndexOf('-');
        return dash < 0 ? locale : locale[..dash] + "-" + locale[(dash + 1)..].ToUpperInvariant();
    }
}
