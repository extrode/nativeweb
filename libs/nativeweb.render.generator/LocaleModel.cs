using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NativeWeb.Render.Generator;

/// <summary>
/// One parsed locales/*.json file: nested objects flatten to dot-joined keys,
/// values must be strings, "_"-prefixed keys are metadata ("_dir"). The JSON
/// parser is hand-rolled because an analyzer must not drag System.Text.Json into
/// the compiler host (spec 003 R14).
/// </summary>
internal sealed class LocaleModel
{
    /// <summary>Lowercase locale id from the filename, e.g. "en", "en-us".</summary>
    public string Id = "";
    public Dictionary<string, string> Strings = new(StringComparer.Ordinal);
    public string? DirOverride;
    public List<string> Errors = [];

    private static readonly HashSet<string> RtlLanguages = new(
        new[] { "ar", "he", "fa", "ur", "ps", "sd", "ug", "yi", "dv", "ckb" },
        StringComparer.Ordinal);

    public string Dir => DirOverride ?? (RtlLanguages.Contains(BaseLanguage) ? "rtl" : "ltr");

    public string BaseLanguage
    {
        get
        {
            int dash = Id.IndexOf('-');
            return dash < 0 ? Id : Id.Substring(0, dash);
        }
    }

    public static LocaleModel Parse(string id, string source)
    {
        LocaleModel model = new() { Id = id };
        try
        {
            int position = 0;
            SkipWhitespace(source, ref position);
            ParseObject(source, ref position, "", model);
            SkipWhitespace(source, ref position);
            if (position < source.Length)
            {
                throw new FormatException($"unexpected content after the root object at offset {position}");
            }
        }
        catch (FormatException e)
        {
            model.Errors.Add(e.Message);
        }

        return model;
    }

    private static void ParseObject(string s, ref int p, string prefix, LocaleModel model)
    {
        Expect(s, ref p, '{');
        SkipWhitespace(s, ref p);
        if (Peek(s, p) == '}')
        {
            p++;
            return;
        }

        while (true)
        {
            SkipWhitespace(s, ref p);
            string name = ParseString(s, ref p);
            SkipWhitespace(s, ref p);
            Expect(s, ref p, ':');
            SkipWhitespace(s, ref p);

            // Metadata is per segment name, not per flattened key: {"app":
            // {"_dir": ...}} is metadata under app, not a string key "app._dir".
            bool isMetadata = name.StartsWith("_", StringComparison.Ordinal);
            string key = prefix.Length == 0 ? name : prefix + "." + name;
            char next = Peek(s, p);
            if (next == '{')
            {
                if (isMetadata)
                {
                    throw new FormatException($"metadata key '{key}' must have a string value, not an object");
                }

                ParseObject(s, ref p, key, model);
            }
            else if (next == '"')
            {
                string value = ParseString(s, ref p);
                if (isMetadata)
                {
                    if (name == "_dir" && prefix.Length == 0)
                    {
                        model.DirOverride = value;
                    }

                    // Other _-metadata is reserved and ignored.
                }
                else if (model.Strings.ContainsKey(key))
                {
                    throw new FormatException($"duplicate key '{key}'");
                }
                else
                {
                    model.Strings[key] = value;
                }
            }
            else
            {
                throw new FormatException($"value of '{key}' must be a string or an object (offset {p})");
            }

            SkipWhitespace(s, ref p);
            char separator = Peek(s, p);
            if (separator == ',')
            {
                p++;
                continue;
            }

            Expect(s, ref p, '}');
            return;
        }
    }

    private static string ParseString(string s, ref int p)
    {
        Expect(s, ref p, '"');
        StringBuilder value = new();
        while (true)
        {
            if (p >= s.Length)
            {
                throw new FormatException("unterminated string");
            }

            char c = s[p++];
            if (c == '"')
            {
                return value.ToString();
            }

            if (c != '\\')
            {
                value.Append(c);
                continue;
            }

            if (p >= s.Length)
            {
                throw new FormatException("unterminated escape sequence");
            }

            char escape = s[p++];
            switch (escape)
            {
                case '"': value.Append('"'); break;
                case '\\': value.Append('\\'); break;
                case '/': value.Append('/'); break;
                case 'b': value.Append('\b'); break;
                case 'f': value.Append('\f'); break;
                case 'n': value.Append('\n'); break;
                case 'r': value.Append('\r'); break;
                case 't': value.Append('\t'); break;
                case 'u':
                    // Not ushort.TryParse(HexNumber): that permits leading and
                    // trailing whitespace, so "\u 41" would parse as U+0041.
                    if (p + 4 > s.Length
                        || !Uri.IsHexDigit(s[p]) || !Uri.IsHexDigit(s[p + 1])
                        || !Uri.IsHexDigit(s[p + 2]) || !Uri.IsHexDigit(s[p + 3]))
                    {
                        throw new FormatException($"invalid \\u escape at offset {p}");
                    }

                    value.Append((char)ushort.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    p += 4;
                    break;
                default:
                    throw new FormatException($"invalid escape '\\{escape}' at offset {p}");
            }
        }
    }

    private static void SkipWhitespace(string s, ref int p)
    {
        while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n'))
        {
            p++;
        }

        // A UTF-8 BOM decoded into the string is whitespace for our purposes.
        if (p < s.Length && s[p] == '﻿')
        {
            p++;
            SkipWhitespace(s, ref p);
        }
    }

    private static char Peek(string s, int p) =>
        p < s.Length ? s[p] : throw new FormatException("unexpected end of file");

    private static void Expect(string s, ref int p, char expected)
    {
        if (p >= s.Length || s[p] != expected)
        {
            throw new FormatException($"expected '{expected}' at offset {p}");
        }

        p++;
    }
}
