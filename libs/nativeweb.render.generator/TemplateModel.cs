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
}

internal sealed class ParseError(string message, ParseErrorKind kind = ParseErrorKind.Block)
{
    public string Message { get; } = message;
    public ParseErrorKind Kind { get; } = kind;

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
    public string? Layout;
    public List<TemplateParam> Params = [];
    public List<Node> Body = [];
    public List<ParseError> Errors = [];
    public List<string> UnknownDirectives = [];
    public List<string> MalformedParams = [];

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

    public static TemplateModel Parse(string path, string source)
    {
        TemplateModel model = new() { Path = path };

        string body = source;
        if (body.Length > 0 && body[0] == '﻿')
        {
            body = body.Substring(1);
        }

        if (body.TrimStart().StartsWith("<!--#template", StringComparison.Ordinal))
        {
            int open = body.IndexOf("<!--#template", StringComparison.Ordinal);
            int close = body.IndexOf("-->", open, StringComparison.Ordinal);
            if (close < 0)
            {
                model.Errors.Add(new ParseError("template header '<!--#template' is never closed with '-->'", ParseErrorKind.Header));
                return model;
            }

            string header = body.Substring(open + "<!--#template".Length, close - open - "<!--#template".Length);
            body = body.Substring(close + "-->".Length);
            if (body.StartsWith("\r\n", StringComparison.Ordinal))
            {
                body = body.Substring(2);
            }
            else if (body.StartsWith("\n", StringComparison.Ordinal))
            {
                body = body.Substring(1);
            }

            foreach (string rawLine in header.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

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
                        model.MalformedParams.Add(decl);
                        continue;
                    }

                    string type = decl.Substring(0, lastSpace).Trim();
                    string name = decl.Substring(lastSpace + 1).Trim();
                    if (name.Length == 0 || !IsIdentifier(name))
                    {
                        model.MalformedParams.Add(decl);
                        continue;
                    }

                    if (ReservedParamNames.Contains(name))
                    {
                        model.Errors.Add(new ParseError(
                            $"@param name '{name}' is reserved (it is the writer parameter or a C# keyword)",
                            ParseErrorKind.Header));
                        continue;
                    }

                    model.Params.Add(new TemplateParam(type, name));
                }
                else
                {
                    model.UnknownDirectives.Add(line);
                }
            }
        }

        ParseBody(model, body);
        return model;
    }

    private static void ParseBody(TemplateModel model, string body)
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

        int position = 0;
        foreach (Match match in TokenPattern.Matches(body))
        {
            if (match.Index > position)
            {
                Current().Add(new StaticNode(body.Substring(position, match.Index - position)));
            }

            position = match.Index + match.Length;
            string token = match.Groups[1].Value.Trim();

            if (token.StartsWith(">", StringComparison.Ordinal))
            {
                ParsePartial(model, Current(), token.Substring(1).Trim());
            }
            else if (token.StartsWith("t:", StringComparison.Ordinal))
            {
                string key = token.Substring(2).Trim();
                if (key.Length == 0 || !IsLocaleKey(key))
                {
                    model.Errors.Add(new ParseError($"'{{{{t:{key}}}}}' is not a valid locale key (letters, digits, '.', '_', '-')"));
                }
                else
                {
                    Current().Add(new LocNode(key));
                }
            }
            else if (token.StartsWith("if ", StringComparison.Ordinal))
            {
                IfNode node = new(CheckExpr(model, token.Substring(3).Trim()));
                Current().Add(node);
                open.Push((node, "if"));
            }
            else if (token == "else")
            {
                if (open.Count == 0 || open.Peek().Kind != "if" || ((IfNode)open.Peek().Owner).InElse)
                {
                    model.Errors.Add(new ParseError("'{{else}}' without a matching '{{if}}'"));
                }
                else
                {
                    ((IfNode)open.Peek().Owner).InElse = true;
                }
            }
            else if (token == "/if")
            {
                PopBlock(model, open, "if", "{{/if}}");
            }
            else if (token.StartsWith("for ", StringComparison.Ordinal))
            {
                string spec = token.Substring(4).Trim();
                int inIndex = spec.IndexOf(" in ", StringComparison.Ordinal);
                if (inIndex <= 0)
                {
                    model.Errors.Add(new ParseError($"'{{{{for {spec}}}}}' must be '{{{{for <name> in <expression>}}}}'"));
                }
                else
                {
                    string variable = spec.Substring(0, inIndex).Trim();
                    string expression = CheckExpr(model, spec.Substring(inIndex + 4).Trim());
                    if (!IsIdentifier(variable))
                    {
                        model.Errors.Add(new ParseError($"'{{{{for}}}}' loop variable '{variable}' is not a valid identifier"));
                    }

                    ForNode node = new(variable, expression);
                    Current().Add(node);
                    open.Push((node, "for"));
                }
            }
            else if (token == "/for")
            {
                PopBlock(model, open, "for", "{{/for}}");
            }
            else if (token.StartsWith("slot? ", StringComparison.Ordinal))
            {
                Current().Add(new SlotNode(token.Substring(6).Trim(), optional: true));
            }
            else if (token.StartsWith("slot ", StringComparison.Ordinal))
            {
                Current().Add(new SlotNode(token.Substring(5).Trim(), optional: false));
            }
            else if (token.StartsWith("fill ", StringComparison.Ordinal))
            {
                FillNode node = new(token.Substring(5).Trim());
                Current().Add(node);
                open.Push((node, "fill"));
            }
            else if (token == "/fill")
            {
                PopBlock(model, open, "fill", "{{/fill}}");
            }
            else
            {
                Current().Add(new ExprNode(CheckExpr(model, token)));
            }
        }

        if (position < body.Length)
        {
            Current().Add(new StaticNode(body.Substring(position)));
        }

        while (open.Count > 0)
        {
            (Node _, string kind) = open.Pop();
            model.Errors.Add(new ParseError($"'{{{{{kind} …}}}}' is never closed with '{{{{/{kind}}}}}'"));
        }
    }

    private static void ParsePartial(TemplateModel model, List<Node> target, string spec)
    {
        // "{{> shared/form-field Label=expr Name=expr}}" — argument values are
        // single space-free tokens; anything needing spaces belongs in a @param
        // computed by the calling C#.
        string[] parts = spec.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            model.Errors.Add(new ParseError("is missing the partial path", ParseErrorKind.Partial)
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
                    ParseErrorKind.Partial)
                {
                    Expression = parts[0],
                });
                continue;
            }

            args.Add(new KeyValuePair<string, string>(
                parts[i].Substring(0, eq),
                CheckExpr(model, parts[i].Substring(eq + 1))));
        }

        target.Add(new PartialNode(parts[0], args));
    }

    private static void PopBlock(TemplateModel model, Stack<(Node Owner, string Kind)> open, string kind, string closer)
    {
        if (open.Count == 0 || open.Peek().Kind != kind)
        {
            model.Errors.Add(new ParseError($"'{closer}' without a matching open block"));
        }
        else
        {
            open.Pop();
        }
    }

    private static string CheckExpr(TemplateModel model, string expression)
    {
        int bad = expression.IndexOfAny(ForbiddenInExpressions);
        if (bad >= 0)
        {
            model.Errors.Add(new ParseError(
                $"expression '{expression}' contains '{expression[bad]}' — template expressions are values, not statements",
                ParseErrorKind.Expression)
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
