using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NativeWeb.Render.Generator;

/// <summary>
/// Build-time pretty-printer (spec 004 R9–R11). Rewrites the static chunks of every
/// composed template so served HTML is indented by element nesting depth, with the
/// blank lines left behind by template directives removed. Runs entirely at
/// generation time: the emitted literals are already formatted, so the per-request
/// render path is byte-for-byte the same work as before.
///
/// Rules:
///  - Only leading whitespace of lines is rewritten (2 spaces per depth); line
///    breaks are never added or removed inside text flow, so inline rendering
///    semantics are untouched.
///  - Whitespace-only lines are dropped (directive residue and authored blanks).
///  - pre/textarea/script/style bodies and comments pass through verbatim.
///  - Partials render as shared methods at call-site-unknown depth, so each partial
///    is emitted once per distinct call-site depth ("RenderAtD{n}" overloads); the
///    caller writes the first line's indent and the partial omits its trailing
///    newline so it splices cleanly mid-stream.
///  - Anything ambiguous (an {{if}} whose branches end at different depths, a
///    {{for}} body that does not return to its starting depth) bails the whole
///    template out to its unformatted form rather than misformatting it.
/// </summary>
internal static class TemplateFormatter
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> VerbatimElements = new(StringComparer.Ordinal)
    {
        "script", "style", "pre", "textarea",
    };

    /// <summary>Formatted variant of one template at one base depth.</summary>
    internal sealed class FormattedBody(List<Node> nodes, Dictionary<PartialNode, int> callDepths)
    {
        public List<Node> Nodes { get; } = nodes;

        /// <summary>Absolute call-site depth per PartialNode instance in <see cref="Nodes"/>.</summary>
        public Dictionary<PartialNode, int> CallDepths { get; } = callDepths;
    }

    internal sealed class FormatPlan
    {
        private readonly Dictionary<string, List<int>> _depths;
        private readonly Dictionary<string, FormattedBody> _bodies;

        internal FormatPlan(Dictionary<string, List<int>> depths, Dictionary<string, FormattedBody> bodies)
        {
            _depths = depths;
            _bodies = bodies;
        }

        /// <summary>Sorted depths for a template; the first is the canonical Render.</summary>
        public IReadOnlyList<int> DepthsFor(string path) =>
            _depths.TryGetValue(path, out List<int>? list) ? list : Zero;

        /// <summary>Null when the template bailed out of formatting.</summary>
        public FormattedBody? BodyFor(string path, int depth) =>
            _bodies.TryGetValue(path + "@" + depth, out FormattedBody? body) ? body : null;

        private static readonly List<int> Zero = [0];
    }

    /// <summary>
    /// Measures every composed template, propagates partial call depths through the
    /// call graph, and produces the formatted bodies the emitter consumes.
    /// </summary>
    public static FormatPlan Plan(IReadOnlyDictionary<string, List<Node>> composedBodies)
    {
        // Measure: relative depth of every partial call, and whether formatting is safe.
        Dictionary<string, List<(string Target, int Depth)>> calls = new(StringComparer.Ordinal);
        HashSet<string> bailed = new(StringComparer.Ordinal);
        HashSet<string> calledAsPartial = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<Node>> entry in composedBodies)
        {
            WalkState state = new() { AtLineStart = true };
            List<(PartialNode Node, int Depth)> found = [];
            WalkNodes(entry.Value, state, output: null, found, callDepths: null);
            if (state.Bailed)
            {
                bailed.Add(entry.Key);
            }

            calls[entry.Key] = [.. found.Select(f => (f.Node.Path, f.Depth))];
            foreach ((PartialNode node, int _) in found)
            {
                calledAsPartial.Add(node.Path);
            }
        }

        // Depth sets: templates nobody calls as a partial are roots at depth 0;
        // partial depths propagate caller-base + call-site-relative until stable.
        Dictionary<string, SortedSet<int>> depths = new(StringComparer.Ordinal);
        foreach (string path in composedBodies.Keys)
        {
            depths[path] = calledAsPartial.Contains(path) ? [] : [0];
        }

        for (int round = 0; round < 20; round++)
        {
            bool changed = false;
            foreach (KeyValuePair<string, List<(string Target, int Depth)>> caller in calls)
            {
                if (bailed.Contains(caller.Key))
                {
                    continue;
                }

                foreach ((string target, int relative) in caller.Value)
                {
                    if (!depths.TryGetValue(target, out SortedSet<int>? set) || set.Count >= 4)
                    {
                        continue; // unknown target or runaway set (cycle guard): callers fall back to Render.
                    }

                    foreach (int baseDepth in depths[caller.Key].ToList())
                    {
                        changed |= set.Add(baseDepth + relative);
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        Dictionary<string, List<int>> depthLists = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, SortedSet<int>> entry in depths)
        {
            depthLists[entry.Key] = entry.Value.Count == 0 ? [0] : [.. entry.Value];
        }

        // Rewrite: one formatted body per (template, depth).
        Dictionary<string, FormattedBody> bodies = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<Node>> entry in composedBodies)
        {
            if (bailed.Contains(entry.Key))
            {
                continue;
            }

            bool isPartial = calledAsPartial.Contains(entry.Key);
            foreach (int depth in depthLists[entry.Key])
            {
                WalkState state = new() { Depth = depth, AtLineStart = !isPartial };
                List<Node> output = [];
                Dictionary<PartialNode, int> callDepths = [];
                WalkNodes(entry.Value, state, output, calls: null, callDepths);
                if (state.Bailed)
                {
                    bailed.Add(entry.Key); // measure/rewrite disagreement: fall back whole template.
                    break;
                }

                if (isPartial)
                {
                    StripTrailingNewline(output);
                }

                bodies[entry.Key + "@" + depth] = new FormattedBody(output, callDepths);
            }

            if (bailed.Contains(entry.Key))
            {
                foreach (int depth in depthLists[entry.Key])
                {
                    bodies.Remove(entry.Key + "@" + depth);
                }
            }
        }

        return new FormatPlan(depthLists, bodies);
    }

    /// <summary>
    /// The scanner state carried across static chunks and dynamic nodes. Branch
    /// joins compare full states: a mismatch means the depth after the block is
    /// data-dependent, which is the bail-out condition.
    /// </summary>
    private sealed class WalkState
    {
        public int Depth;
        public bool AtLineStart;
        public bool InTag;
        public char Quote;
        public int TagEffect;
        public bool TagSelfClose;
        public string? PendingVerbatim;
        public string? Verbatim;
        public bool InComment;
        public bool Bailed;

        public WalkState Clone() => (WalkState)MemberwiseClone();

        public void CopyFrom(WalkState other)
        {
            Depth = other.Depth;
            AtLineStart = other.AtLineStart;
            InTag = other.InTag;
            Quote = other.Quote;
            TagEffect = other.TagEffect;
            TagSelfClose = other.TagSelfClose;
            PendingVerbatim = other.PendingVerbatim;
            Verbatim = other.Verbatim;
            InComment = other.InComment;
            Bailed = other.Bailed;
        }

        public bool SameAs(WalkState other) =>
            Depth == other.Depth
            && AtLineStart == other.AtLineStart
            && InTag == other.InTag
            && Quote == other.Quote
            && TagEffect == other.TagEffect
            && TagSelfClose == other.TagSelfClose
            && PendingVerbatim == other.PendingVerbatim
            && Verbatim == other.Verbatim
            && InComment == other.InComment;
    }

    private static void WalkNodes(
        List<Node> nodes,
        WalkState state,
        List<Node>? output,
        List<(PartialNode Node, int Depth)>? calls,
        Dictionary<PartialNode, int>? callDepths)
    {
        foreach (Node node in nodes)
        {
            if (state.Bailed)
            {
                return;
            }

            switch (node)
            {
                case StaticNode s:
                    StringBuilder? text = output is null ? null : new StringBuilder(s.Text.Length + 16);
                    ProcessStatic(s.Text, state, text);
                    if (output is not null && text!.Length > 0)
                    {
                        AddStatic(output, text.ToString());
                    }

                    break;
                case ExprNode:
                case LocNode:
                    EmitLineStartIndent(state, output);
                    output?.Add(node);
                    break;
                case IfNode i:
                    WalkState thenState = state.Clone();
                    List<Node>? thenOut = output is null ? null : [];
                    WalkNodes(i.Then, thenState, thenOut, calls, callDepths);
                    WalkState elseState = state.Clone();
                    List<Node>? elseOut = output is null ? null : [];
                    WalkNodes(i.Else, elseState, elseOut, calls, callDepths);
                    if (thenState.Bailed || elseState.Bailed || !thenState.SameAs(elseState))
                    {
                        state.Bailed = true;
                        return;
                    }

                    state.CopyFrom(thenState);
                    if (output is not null)
                    {
                        IfNode ifClone = new(i.Condition) { InElse = false };
                        ifClone.Then.AddRange(thenOut!);
                        ifClone.Else.AddRange(elseOut!);
                        output.Add(ifClone);
                    }

                    break;
                case ForNode f:
                    WalkState bodyState = state.Clone();
                    List<Node>? bodyOut = output is null ? null : [];
                    WalkNodes(f.Body, bodyState, bodyOut, calls, callDepths);
                    if (bodyState.Bailed || !bodyState.SameAs(state))
                    {
                        state.Bailed = true;
                        return;
                    }

                    if (output is not null)
                    {
                        ForNode forClone = new(f.Variable, f.Expression);
                        forClone.Body.AddRange(bodyOut!);
                        output.Add(forClone);
                    }

                    break;
                case PartialNode p:
                    EmitLineStartIndent(state, output);
                    calls?.Add((p, state.Depth));
                    if (output is not null)
                    {
                        PartialNode partialClone = new(p.Path, p.Args);
                        callDepths![partialClone] = state.Depth;
                        output.Add(partialClone);
                    }

                    break;
                default:
                    // Slot/Fill are gone after inlining; pass through defensively.
                    output?.Add(node);
                    break;
            }
        }
    }

    /// <summary>A dynamic node at the start of a line gets the line's indent written by the caller.</summary>
    private static void EmitLineStartIndent(WalkState state, List<Node>? output)
    {
        if (state.AtLineStart && !state.InTag && state.Verbatim is null && !state.InComment)
        {
            if (output is not null && state.Depth > 0)
            {
                AddStatic(output, Indent(state.Depth));
            }

            state.AtLineStart = false;
        }
    }

    private static void ProcessStatic(string text, WalkState state, StringBuilder? output)
    {
        int length = text.Length;
        int i = 0;
        while (i < length)
        {
            char c = text[i];

            if (state.InComment)
            {
                int end = text.IndexOf("-->", i, StringComparison.Ordinal);
                if (end < 0)
                {
                    output?.Append(text, i, length - i);
                    i = length;
                }
                else
                {
                    output?.Append(text, i, end + 3 - i);
                    i = end + 3;
                    state.InComment = false;
                }

                continue;
            }

            if (state.Verbatim is not null)
            {
                int end = IndexOfIgnoreCase(text, state.Verbatim, i);
                if (end < 0)
                {
                    output?.Append(text, i, length - i);
                    i = length;
                }
                else
                {
                    output?.Append(text, i, end - i);
                    i = end;
                    state.Verbatim = null; // the close tag itself is scanned normally below.
                }

                continue;
            }

            if (state.InTag)
            {
                if (state.Quote != '\0')
                {
                    if (c == state.Quote)
                    {
                        state.Quote = '\0';
                    }

                    output?.Append(c);
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                    case '\'':
                        state.Quote = c;
                        output?.Append(c);
                        i++;
                        break;
                    case '/':
                        state.TagSelfClose = true;
                        output?.Append(c);
                        i++;
                        break;
                    case '>':
                        state.InTag = false;
                        if (!state.TagSelfClose)
                        {
                            state.Depth = Math.Max(0, state.Depth + state.TagEffect);
                            state.Verbatim = state.PendingVerbatim;
                        }

                        state.TagSelfClose = false;
                        state.TagEffect = 0;
                        state.PendingVerbatim = null;
                        output?.Append(c);
                        i++;
                        break;
                    case '\r':
                        i++;
                        break;
                    case '\n':
                        // Attribute continuation line: one level past the tag's own line.
                        TrimTrailingSpaces(output);
                        output?.Append('\n').Append(Indent(state.Depth + 1));
                        i++;
                        while (i < length && (text[i] == ' ' || text[i] == '\t'))
                        {
                            i++;
                        }

                        break;
                    default:
                        if (c is not ' ' and not '\t')
                        {
                            state.TagSelfClose = false;
                        }

                        output?.Append(c);
                        i++;
                        break;
                }

                continue;
            }

            if (state.AtLineStart)
            {
                if (c is ' ' or '\t' or '\r' or '\n')
                {
                    i++; // authored indentation and blank lines are dropped and rebuilt.
                    continue;
                }

                int lineDepth = state.Depth;
                if (c == '<' && i + 1 < length && text[i + 1] == '/')
                {
                    lineDepth--;
                }

                if (lineDepth > 0)
                {
                    output?.Append(Indent(lineDepth));
                }

                state.AtLineStart = false;
                continue; // reprocess c in normal mode.
            }

            if (c == '<')
            {
                if (i + 3 < length && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
                {
                    state.InComment = true;
                    output?.Append("<!--");
                    i += 4;
                    continue;
                }

                if (i + 1 < length && text[i + 1] == '!')
                {
                    state.InTag = true;
                    state.TagEffect = 0;
                    output?.Append(c);
                    i++;
                    continue;
                }

                if (i + 1 < length && text[i + 1] == '/')
                {
                    state.InTag = true;
                    state.TagEffect = -1;
                    output?.Append(c);
                    i++;
                    continue;
                }

                int nameEnd = i + 1;
                while (nameEnd < length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '-' || text[nameEnd] == ':'))
                {
                    nameEnd++;
                }

                if (nameEnd == i + 1)
                {
                    output?.Append(c); // a stray '<' in text.
                    i++;
                    continue;
                }

                string name = text.Substring(i + 1, nameEnd - i - 1).ToLowerInvariant();
                state.InTag = true;
                state.TagEffect = VoidElements.Contains(name) ? 0 : 1;
                state.PendingVerbatim = VerbatimElements.Contains(name) ? "</" + name : null;
                output?.Append(text, i, nameEnd - i);
                i = nameEnd;
                continue;
            }

            if (c == '\n')
            {
                TrimTrailingSpaces(output);
                output?.Append('\n');
                state.AtLineStart = true;
                i++;
                continue;
            }

            if (c == '\r')
            {
                i++;
                continue;
            }

            output?.Append(c);
            i++;
        }
    }

    private static void AddStatic(List<Node> output, string text)
    {
        if (output.Count > 0 && output[output.Count - 1] is StaticNode last)
        {
            output[output.Count - 1] = new StaticNode(last.Text + text);
        }
        else
        {
            output.Add(new StaticNode(text));
        }
    }

    /// <summary>Partials splice mid-stream: the caller's following chunk supplies the newline.</summary>
    private static void StripTrailingNewline(List<Node> output)
    {
        if (output.Count == 0 || output[output.Count - 1] is not StaticNode last)
        {
            return;
        }

        string text = last.Text.TrimEnd(' ', '\t');
        if (text.EndsWith("\n", StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - 1);
        }

        if (text.Length == 0)
        {
            output.RemoveAt(output.Count - 1);
        }
        else
        {
            output[output.Count - 1] = new StaticNode(text);
        }
    }

    private static void TrimTrailingSpaces(StringBuilder? output)
    {
        if (output is null)
        {
            return;
        }

        while (output.Length > 0 && output[output.Length - 1] is ' ' or '\t')
        {
            output.Length--;
        }
    }

    private static string Indent(int depth) => new(' ', 2 * Math.Max(0, depth));

    private static int IndexOfIgnoreCase(string text, string value, int start) =>
        text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
}
