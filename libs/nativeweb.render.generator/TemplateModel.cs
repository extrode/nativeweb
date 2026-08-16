using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NativeWeb.Render.Generator;

// The parsed shape of one template file. Parsing is deliberately regex-free where
// it matters and tolerant of nothing: every irregularity becomes an error record
// that the generator surfaces as a diagnostic (R12).

internal sealed class TemplateParam(string type, string name)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
}

internal abstract class Node
{
    /// <summary>
    /// Character offset of the token that produced this node, in the original
    /// file including its header and any BOM, or -1 when unknown. Carried so a
    /// diagnostic raised long after parsing — a partial that resolves to
    /// nothing, a slot with no fill — can still point at the line that wrote it.
    /// </summary>
    public int Offset { get; set; } = -1;
}

internal sealed class StaticNode(string text) : Node
{
    public string Text { get; } = text;
}

internal sealed class ExprNode(string expression) : Node
{
    public string Expression { get; } = expression;
}

internal sealed class LocNode(string key) : Node
{
    public string Key { get; } = key;
}

internal sealed class IfNode(string condition) : Node
{
    public string Condition { get; } = condition;
    public List<Node> Then { get; } = [];
    public List<Node> Else { get; } = [];
    public bool InElse { get; set; }
}

internal sealed class ForNode(string variable, string expression) : Node
{
    public string Variable { get; } = variable;
    public string Expression { get; } = expression;
    public List<Node> Body { get; } = [];
}

internal sealed class PartialNode(string path, List<KeyValuePair<string, string>> args) : Node
{
    public string Path { get; } = path;
    public List<KeyValuePair<string, string>> Args { get; } = args;
}

internal sealed class SlotNode(string name, bool optional) : Node
{
    public string Name { get; } = name;
    public bool Optional { get; } = optional;
}

internal sealed class FillNode(string name) : Node
{
    public string Name { get; } = name;
    public List<Node> Body { get; } = [];
}

internal enum ParseErrorKind
{
    /// <summary>Unbalanced or misplaced block token — NWT008.</summary>
    Block,

    /// <summary>Broken template header — NWT001.</summary>
    Header,

    /// <summary>Rejected expression content — NWT012.</summary>
    Expression,

    /// <summary>Malformed partial call — NWT006.</summary>
    Partial,

    /// <summary>Interpolation inside a tag but outside a quoted attribute value — NWT016.</summary>
    LooseAttribute,
}

internal sealed class ParseError(string message, ParseErrorKind kind = ParseErrorKind.Block, int offset = -1)
{
    public string Message { get; } = message;
    public ParseErrorKind Kind { get; } = kind;

    /// <summary>
    /// Character offset in the original file, or -1 when the error has no single
    /// position — an unclosed block is discovered at end of file, and pointing at
    /// the last character would be worse than pointing at the file.
    /// </summary>
    public int Offset { get; } = offset;

    /// <summary>
    /// The offending expression (<see cref="ParseErrorKind.Expression"/>) or the
    /// partial path (<see cref="ParseErrorKind.Partial"/>).
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>The rejected character, set only for <see cref="ParseErrorKind.Expression"/>.</summary>
    public char OffendingChar { get; set; }
}

internal sealed class TemplateModel
{
    /// <summary>Logical path relative to the templates root, e.g. "auth/sign-in".</summary>
    public string Path = "";

    /// <summary>
    /// The file on disk, as the compiler was given it. Only diagnostics use this:
    /// everything else in the generator addresses templates logically, but a
    /// diagnostic has to name a path an editor can open.
    /// </summary>
    public string PhysicalPath = "";

    /// <summary>The unmodified file text, kept so an offset can be turned into a line and column.</summary>
    public string Source = "";

    public string? Layout;
    public List<TemplateParam> Params = [];
    public List<Node> Body = [];
    public List<ParseError> Errors = [];
    public List<(string Text, int Offset)> UnknownDirectives = [];
    public List<(string Text, int Offset)> MalformedParams = [];

    // No RegexOptions.Compiled: emitting dynamic assemblies from an analyzer host
    // is the documented anti-pattern, and the interpreted engine is fine here.
    private static readonly Regex TokenPattern = new(@"\{\{(.*?)\}\}", RegexOptions.Singleline);

    // Parameter names that would break the generated Render method: the writer
    // and implicit locale parameters plus every C# keyword.
    private static readonly HashSet<string> ReservedParamNames = new(
        ("writer locale abstract as base bool break byte case catch char checked class const continue decimal default " +
         "delegate do double else enum event explicit extern false finally fixed float for foreach goto if " +
         "implicit in int interface internal is lock long namespace new null object operator out override params " +
         "private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct " +
         "switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while")
            .Split(' '),
        StringComparer.Ordinal);

    // Characters that would let a template expression escape "value" territory and
    // smuggle statements into the generated method (R12/NWT012).
    private static readonly char[] ForbiddenInExpressions = [';', '{', '}'];

    private static readonly char[] SpaceSeparator = [' '];

    public static TemplateModel Parse(string path, string physicalPath, string source)
    {
        TemplateModel model = new() { Path = path, PhysicalPath = physicalPath, Source = source };

        // Every offset recorded below is into `source`, not into `body`: `body`
        // is progressively sliced (BOM, header, the newline after it) and a
        // diagnostic has to point into the file the author actually opens.
        string body = source;
        int bodyOffset = 0;
        if (body.Length > 0 && body[0] == '﻿')
        {
            body = body.Substring(1);
            bodyOffset = 1;
        }

        if (body.TrimStart().StartsWith("<!--#template", StringComparison.Ordinal))
        {
            int open = body.IndexOf("<!--#template", StringComparison.Ordinal);
            int close = body.IndexOf("-->", open, StringComparison.Ordinal);
            if (close < 0)
            {
                model.Errors.Add(new ParseError(
                    "template header '<!--#template' is never closed with '-->'",
                    ParseErrorKind.Header,
                    bodyOffset + open));
                return model;
            }

            int headerStart = open + "<!--#template".Length;
            string header = body.Substring(headerStart, close - headerStart);
            int consumed = close + "-->".Length;
            body = body.Substring(consumed);
            if (body.StartsWith("\r\n", StringComparison.Ordinal))
            {
                body = body.Substring(2);
                consumed += 2;
            }
            else if (body.StartsWith("\n", StringComparison.Ordinal))
            {
                body = body.Substring(1);
                consumed += 1;
            }

            int lineStart = bodyOffset + headerStart;
            bodyOffset += consumed;

            foreach (string rawLine in header.Split('\n'))
            {
                int thisLine = lineStart;
                lineStart += rawLine.Length + 1;

                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                // Point at the directive, not at the indentation before it.
                int at = thisLine + (rawLine.Length - rawLine.TrimStart().Length);

                if (line.StartsWith("@layout ", StringComparison.Ordinal))
                {
                    model.Layout = line.Substring("@layout ".Length).Trim();
                }
                else if (line.StartsWith("@param ", StringComparison.Ordinal))
                {
                    string decl = line.Substring("@param ".Length).Trim();
                    int lastSpace = decl.LastIndexOf(' ');
                    if (lastSpace <= 0)
                    {
                        model.MalformedParams.Add((decl, at));
                        continue;
                    }

                    string type = decl.Substring(0, lastSpace).Trim();
                    string name = decl.Substring(lastSpace + 1).Trim();
                    if (name.Length == 0 || !IsIdentifier(name))
                    {
                        model.MalformedParams.Add((decl, at));
                        continue;
                    }

                    // The type was previously copied into the generated method
                    // signature unchecked, so '@param My Model m' split as type
                    // "My Model" and surfaced as a C# syntax error in a file the
                    // author never wrote. NWT003 names the template line instead.
                    if (!IsTypeName(type))
                    {
                        model.MalformedParams.Add((decl, at));
                        continue;
                    }

                    if (ReservedParamNames.Contains(name))
                    {
                        model.Errors.Add(new ParseError(
                            $"@param name '{name}' is reserved (it is the writer parameter or a C# keyword)",
                            ParseErrorKind.Header,
                            at));
                        continue;
                    }

                    model.Params.Add(new TemplateParam(type, name));
                }
                else
                {
                    model.UnknownDirectives.Add((line, at));
                }
            }
        }

        ParseBody(model, body, bodyOffset);
        return model;
    }

    private static void ParseBody(TemplateModel model, string body, int bodyOffset)
    {
        // A stack of open blocks; the top's list receives new nodes.
        Stack<(Node Owner, string Kind)> open = new();
        List<Node> Current() => open.Count == 0
            ? model.Body
            : open.Peek() switch
            {
                (IfNode ifNode, "if") => ifNode.InElse ? ifNode.Else : ifNode.Then,
                (ForNode forNode, "for") => forNode.Body,
                (FillNode fillNode, "fill") => fillNode.Body,
                _ => model.Body,
            };

        HashSet<int> looseInTag = LooseInTagOffsets(body);

        int position = 0;
        foreach (Match match in TokenPattern.Matches(body))
        {
            if (match.Index > position)
            {
                Current().Add(new StaticNode(body.Substring(position, match.Index - position)));
            }

            position = match.Index + match.Length;
            string token = match.Groups[1].Value.Trim();
            int at = bodyOffset + match.Index;

            if (token.StartsWith(">", StringComparison.Ordinal))
            {
                ParsePartial(model, Current(), token.Substring(1).Trim(), at);
            }
            else if (token.StartsWith("t:", StringComparison.Ordinal))
            {
                string key = token.Substring(2).Trim();
                if (key.Length == 0 || !IsLocaleKey(key))
                {
                    model.Errors.Add(new ParseError($"'{{{{t:{key}}}}}' is not a valid locale key (letters, digits, '.', '_', '-')", ParseErrorKind.Block, at));
                }
                else
                {
                    CheckAttributeContext(model, looseInTag, match.Index, at, $"{{{{t:{key}}}}}");
                    Current().Add(new LocNode(key) { Offset = at });
                }
            }
            else if (token.StartsWith("if ", StringComparison.Ordinal))
            {
                IfNode node = new(CheckExpr(model, token.Substring(3).Trim(), at)) { Offset = at };
                Current().Add(node);
                open.Push((node, "if"));
            }
            else if (token == "else")
            {
                if (open.Count == 0 || open.Peek().Kind != "if" || ((IfNode)open.Peek().Owner).InElse)
                {
                    model.Errors.Add(new ParseError("'{{else}}' without a matching '{{if}}'", ParseErrorKind.Block, at));
                }
                else
                {
                    ((IfNode)open.Peek().Owner).InElse = true;
                }
            }
            else if (token == "/if")
            {
                PopBlock(model, open, "if", "{{/if}}", at);
            }
            else if (token.StartsWith("for ", StringComparison.Ordinal))
            {
                string spec = token.Substring(4).Trim();
                int inIndex = spec.IndexOf(" in ", StringComparison.Ordinal);
                if (inIndex <= 0)
                {
                    model.Errors.Add(new ParseError($"'{{{{for {spec}}}}}' must be '{{{{for <name> in <expression>}}}}'", ParseErrorKind.Block, at));
                }
                else
                {
                    string variable = spec.Substring(0, inIndex).Trim();
                    string expression = CheckExpr(model, spec.Substring(inIndex + 4).Trim(), at);
                    if (!IsIdentifier(variable) || ReservedParamNames.Contains(variable))
                    {
                        model.Errors.Add(new ParseError($"'{{{{for}}}}' loop variable '{variable}' is not a valid identifier or is reserved", ParseErrorKind.Block, at));
                    }

                    ForNode node = new(variable, expression) { Offset = at };
                    Current().Add(node);
                    open.Push((node, "for"));
                }
            }
            else if (token == "/for")
            {
                PopBlock(model, open, "for", "{{/for}}", at);
            }
            else if (token.StartsWith("slot? ", StringComparison.Ordinal))
            {
                Current().Add(new SlotNode(token.Substring(6).Trim(), optional: true) { Offset = at });
            }
            else if (token.StartsWith("slot ", StringComparison.Ordinal))
            {
                Current().Add(new SlotNode(token.Substring(5).Trim(), optional: false) { Offset = at });
            }
            else if (token.StartsWith("fill ", StringComparison.Ordinal))
            {
                FillNode node = new(token.Substring(5).Trim()) { Offset = at };
                Current().Add(node);
                open.Push((node, "fill"));
            }
            else if (token == "/fill")
            {
                PopBlock(model, open, "fill", "{{/fill}}", at);
            }
            else
            {
                CheckAttributeContext(model, looseInTag, match.Index, at, $"{{{{{token}}}}}");
                Current().Add(new ExprNode(CheckExpr(model, token, at)) { Offset = at });
            }
        }

        if (position < body.Length)
        {
            Current().Add(new StaticNode(body.Substring(position)));
        }

        while (open.Count > 0)
        {
            (Node owner, string kind) = open.Pop();
            model.Errors.Add(new ParseError(
                $"'{{{{{kind} …}}}}' is never closed with '{{{{/{kind}}}}}'",
                ParseErrorKind.Block,
                owner.Offset));
        }
    }

    private static void CheckAttributeContext(TemplateModel model, HashSet<int> looseInTag, int index, int at, string token)
    {
        if (looseInTag.Contains(index))
        {
            model.Errors.Add(new ParseError(
                $"'{token}' is interpolated inside a tag but not inside a quoted attribute value",
                ParseErrorKind.LooseAttribute,
                at)
            {
                Expression = token,
            });
        }
    }

    /// <summary>
    /// Offsets of <c>{{</c> tokens that sit inside a tag without being inside a
    /// quoted attribute value — an unquoted value (<c>class={{X}}</c>) or the
    /// attribute-name position (<c>&lt;div {{X}}&gt;</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TemplateWriter</c> encodes exactly <c>&lt;&gt;&amp;"'</c>, and its comment
    /// states plainly that this is safe "in text and QUOTED-attribute contexts,
    /// which are the only contexts templates may interpolate into". Nothing
    /// enforced that. An unquoted value is terminated by a space, and a space is
    /// not encoded, so one interpolated value could close the attribute and open
    /// <c>onfocus=…</c>; the attribute-name position is the same escape by a
    /// shorter route. Making the invariant checkable is the whole point — a
    /// safety property asserted only in prose is a property that holds until
    /// someone writes the line that breaks it.
    /// </para>
    /// <para>
    /// One pass, and deliberately not a tokenizer: it walks tags, skips comments,
    /// and jumps over every <c>{{…}}</c> region wholesale. That last part is not
    /// an optimisation. An expression is C#, not markup, and reading its bytes as
    /// markup invents structure that is not there: <c>{{V.Replace("&lt;b class", "")}}</c>
    /// opens a tag nothing closes, after which every later interpolation looks
    /// like it sits inside one. Quotes turn out to be harmless either way — a
    /// quote only opens a value when it follows <c>=</c> — but tags do not, and
    /// the honest rule is that expression text is never scanned at all.
    /// </para>
    /// </remarks>
    private static HashSet<int> LooseInTagOffsets(string body)
    {
        HashSet<int> loose = [];

        bool inTag = false;
        char quote = '\0';
        bool afterEquals = false;
        bool inUnquotedValue = false;

        int i = 0;
        while (i < body.Length)
        {
            if (i + 1 < body.Length && body[i] == '{' && body[i + 1] == '{')
            {
                if (inTag && quote == '\0')
                {
                    loose.Add(i);
                }

                int end = body.IndexOf("}}", i + 2, StringComparison.Ordinal);
                i = end < 0 ? body.Length : end + 2;
                continue;
            }

            char c = body[i];

            if (!inTag)
            {
                if (c == '<')
                {
                    if (string.CompareOrdinal(body, i, "<!--", 0, 4) == 0)
                    {
                        int end = body.IndexOf("-->", i + 4, StringComparison.Ordinal);
                        i = end < 0 ? body.Length : end + 3;
                        continue;
                    }

                    if (i + 1 < body.Length && (char.IsLetter(body[i + 1]) || body[i + 1] == '/'))
                    {
                        inTag = true;
                        quote = '\0';
                        afterEquals = false;
                        inUnquotedValue = false;
                    }
                }

                i++;
                continue;
            }

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c == '>')
            {
                inTag = false;
                afterEquals = false;
                inUnquotedValue = false;
            }
            else if (c == '=')
            {
                afterEquals = true;
                inUnquotedValue = false;
            }
            else if (afterEquals)
            {
                // Whitespace between '=' and the value is legal HTML, so keep
                // waiting rather than concluding the value is unquoted.
                if (c is '"' or '\'')
                {
                    quote = c;
                    afterEquals = false;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    afterEquals = false;
                    inUnquotedValue = true;
                }
            }
            else if (inUnquotedValue && char.IsWhiteSpace(c))
            {
                inUnquotedValue = false;
            }

            i++;
        }

        return loose;
    }

    private static void ParsePartial(TemplateModel model, List<Node> target, string spec, int at)
    {
        // "{{> shared/form-field Label=expr Name=expr}}" — argument values are
        // single space-free tokens; anything needing spaces belongs in a @param
        // computed by the calling C#.
        string[] parts = spec.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            model.Errors.Add(new ParseError("is missing the partial path", ParseErrorKind.Partial, at)
            {
                Expression = "{{> }}",
            });
            return;
        }

        List<KeyValuePair<string, string>> args = [];
        for (int i = 1; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            if (eq <= 0)
            {
                model.Errors.Add(new ParseError(
                    $"has malformed argument '{parts[i]}' — arguments are 'Name=expression' with no spaces in the expression",
                    ParseErrorKind.Partial,
                    at)
                {
                    Expression = parts[0],
                });
                continue;
            }

            args.Add(new KeyValuePair<string, string>(
                parts[i].Substring(0, eq),
                CheckExpr(model, parts[i].Substring(eq + 1), at)));
        }

        target.Add(new PartialNode(parts[0], args) { Offset = at });
    }

    private static void PopBlock(TemplateModel model, Stack<(Node Owner, string Kind)> open, string kind, string closer, int at)
    {
        if (open.Count == 0 || open.Peek().Kind != kind)
        {
            model.Errors.Add(new ParseError($"'{closer}' without a matching open block", ParseErrorKind.Block, at));
        }
        else
        {
            open.Pop();
        }
    }

    private static string CheckExpr(TemplateModel model, string expression, int at)
    {
        int bad = expression.IndexOfAny(ForbiddenInExpressions);
        if (bad >= 0)
        {
            model.Errors.Add(new ParseError(
                $"expression '{expression}' contains '{expression[bad]}' — template expressions are values, not statements",
                ParseErrorKind.Expression,
                at)
            {
                Expression = expression,
                OffendingChar = expression[bad],
            });
        }

        return expression;
    }

    private static bool IsLocaleKey(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A cheap plausibility check on a <c>@param</c> type, not a C# type parser.
    /// It exists to catch the one mistake that used to escape the generator and
    /// reappear as a compiler error inside generated code — a type with a space
    /// in it, because <c>@param</c> splits on the LAST space and everything to
    /// its left becomes the type. Deliberately permissive about everything else:
    /// generics, arrays, nullable annotations, dotted and <c>global::</c>-qualified
    /// names all pass, and a name that is well-formed but does not exist is still
    /// the C# compiler's to reject, with a message it words better than we could.
    /// </summary>
    private static bool IsTypeName(string value)
    {
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        foreach (char c in value)
        {
            bool ok = char.IsLetterOrDigit(c)
                || c == '_' || c == '.' || c == ':'
                || c == '<' || c == '>' || c == ','
                || c == '[' || c == ']' || c == '?';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
