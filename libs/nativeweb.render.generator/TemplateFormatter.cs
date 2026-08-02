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
///    breaks are never added or removed inside text flow. Accepted limitation:
///    dropping whitespace-only lines and rewriting leading indent does change
///    rendered output under CSS white-space: pre-line / pre-wrap on non-verbatim
///    elements — templates relying on that belong in a verbatim element.
///  - Whitespace-only lines are dropped (directive residue and authored blanks).
///  - pre/textarea/script/style bodies and comments pass through verbatim; their
///    close tags keep their authored column, because indenting them would inject
///    characters into pre/textarea content.
///  - Every template's Render is its standalone shape, formatted at depth 0 with
///    its trailing newline kept, independent of any call graph. Each distinct
///    partial call-site depth gets a RenderAtD{n} overload whose first line is
///    unindented (the caller writes it) and whose trailing newline is stripped
///    (the caller's following chunk supplies it), so it splices mid-stream.
///  - A partial call splices the callee's net effect into the caller's walk: a
///    callee ending cleanly shifts the caller's depth by its net tag balance; a
///    callee ending inside a tag, comment, quote or verbatim element — or one
///    that itself bailed — bails the caller. Bails cascade up the call graph.
///  - Anything ambiguous (an {{if}} whose branches end at different depths, a
///    {{for}} body that does not return to its starting depth, a partial cycle,
///    or a depth set past its cap) bails the whole template out to its
///    unformatted form rather than misformatting it.
///  - A call site whose depth is negative (the caller dipped below the document
///    root before the call) has no representable overload, so the caller bails.
///  - A callee whose splice body strips to nothing is unclean: splicing it would
///    leave a blank or trailing-space line at the call site, so callers bail.
/// </summary>
internal static class TemplateFormatter
{
    // The depth-set accumulation is iterative: call-site depths from callers that
    // later bail still consume slots before convergence, so the cap must leave
    // headroom above the real variant count (manzil converges on 3 variants but
    // needs 8 slots to get there). Each variant is one small RenderAtD method.
    private const int MaxDepthVariants = 8;

    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> VerbatimElements = new(StringComparer.Ordinal)
    {
        "script", "style", "pre", "textarea",
    };

    /// <summary>Formatted variant of one template at one shape/depth.</summary>
    internal sealed class FormattedBody(List<Node> nodes, Dictionary<PartialNode, int> callDepths)
    {
        public List<Node> Nodes { get; } = nodes;

        /// <summary>Absolute call-site depth per PartialNode instance in <see cref="Nodes"/>.</summary>
        public Dictionary<PartialNode, int> CallDepths { get; } = callDepths;
    }

    internal sealed class FormatPlan
    {
        private readonly Dictionary<string, List<int>> _partialDepths;
        private readonly Dictionary<string, FormattedBody> _bodies;

        internal FormatPlan(Dictionary<string, List<int>> partialDepths, Dictionary<string, FormattedBody> bodies)
        {
            _partialDepths = partialDepths;
            _bodies = bodies;
        }

        /// <summary>The standalone (page-shape) body; null when the template bailed.</summary>
        public FormattedBody? StandaloneBody(string path) =>
            _bodies.TryGetValue(path + "@S", out FormattedBody? body) ? body : null;

        /// <summary>Sorted distinct call-site depths; empty when never called or bailed.</summary>
        public IReadOnlyList<int> PartialDepths(string path) =>
            _partialDepths.TryGetValue(path, out List<int>? list) ? list : None;

        /// <summary>The splice-shape body for one call-site depth; null when the template bailed.</summary>
        public FormattedBody? PartialBodyFor(string path, int depth) =>
            _bodies.TryGetValue(path + "@" + depth, out FormattedBody? body) ? body : null;

        private static readonly List<int> None = [];
    }

    /// <summary>What one template's walk found: its partial calls, and its net effect on a caller.</summary>
    private sealed class Measure
    {
        public List<(string Target, int Depth)> Calls = [];
        public int Delta;

        /// <summary>Whether the splice body's rendered output ends with a newline (its
        /// trailing newline is only stripped when the last node is a static chunk).</summary>
        public bool EndsWithNewline;

        /// <summary>False when the callee cannot be spliced into a formatted caller:
        /// it bailed, ends inside a tag/comment/quote/verbatim element, or its splice
        /// body would emit a leading newline the caller's indent cannot absorb.</summary>
        public bool Clean;

        public bool Bailed;
    }

    /// <summary>
    /// Measures every composed template, propagates partial call depths through the
    /// call graph, and produces the formatted bodies the emitter consumes.
    /// </summary>
    public static FormatPlan Plan(IReadOnlyDictionary<string, List<Node>> composedBodies)
    {
        Dictionary<string, Measure> measures = new(StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);
        foreach (string path in composedBodies.Keys)
        {
            MeasureOf(path, composedBodies, measures, visiting);
        }

        HashSet<string> bailed = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Measure> entry in measures)
        {
            if (entry.Value.Bailed)
            {
                bailed.Add(entry.Key);
            }
        }

        // Partial depth sets: every template's standalone body is a caller at base 0,
        // and each of its own partial depths is a further base; propagate until stable.
        Dictionary<string, SortedSet<int>> depths = new(StringComparer.Ordinal);
        foreach (string path in composedBodies.Keys)
        {
            depths[path] = [];
        }

        bool converged = false;
        for (int round = 0; round < 20; round++)
        {
            bool changed = false;
            foreach (KeyValuePair<string, Measure> caller in measures)
            {
                if (bailed.Contains(caller.Key))
                {
                    continue;
                }

                foreach ((string target, int relative) in caller.Value.Calls)
                {
                    if (!depths.TryGetValue(target, out SortedSet<int>? set))
                    {
                        continue; // unknown target: the cascade below bails the caller.
                    }

                    foreach (int baseDepth in BasesOf(caller.Key, depths))
                    {
                        int absolute = baseDepth + relative;
                        if (absolute < 0)
                        {
                            continue; // no RenderAtD{-n}: the cascade below bails the caller.
                        }

                        if (set.Count >= MaxDepthVariants && !set.Contains(absolute))
                        {
                            continue; // capped: the cascade below bails the caller.
                        }

                        changed |= set.Add(absolute);
                    }
                }
            }

            if (!changed)
            {
                converged = true;
                break;
            }
        }

        if (!converged)
        {
            // A still-moving fixpoint means depth sets we cannot trust: format nothing.
            return new FormatPlan(new Dictionary<string, List<int>>(StringComparer.Ordinal), new Dictionary<string, FormattedBody>(StringComparer.Ordinal));
        }

        // Cascade: a caller whose callee bailed, is unknown, or lacks the needed depth
        // variant cannot splice formatted output, so it bails too — repeatedly, since
        // each new bail can invalidate its own callers.
        CascadeBails(measures, depths, bailed);

        // Rewrite: one standalone body per template plus one body per call-site depth.
        // A rewrite-time bail (measure/rewrite disagreement, defensively) re-cascades.
        Dictionary<string, FormattedBody> bodies = new(StringComparer.Ordinal);
        HashSet<string> built = new(StringComparer.Ordinal);
        while (true)
        {
            bool newBail = false;
            foreach (KeyValuePair<string, List<Node>> entry in composedBodies)
            {
                if (bailed.Contains(entry.Key) || !built.Add(entry.Key))
                {
                    continue;
                }

                if (!TryBuildBodies(entry.Key, entry.Value, depths[entry.Key], measures, bailed, bodies))
                {
                    bailed.Add(entry.Key);
                    newBail = true;
                }
            }

            if (!newBail)
            {
                break;
            }

            CascadeBails(measures, depths, bailed);
        }

        foreach (string path in bailed)
        {
            bodies.Remove(path + "@S");
            if (depths.TryGetValue(path, out SortedSet<int>? set))
            {
                foreach (int depth in set)
                {
                    bodies.Remove(path + "@" + depth);
                }
            }
        }

        Dictionary<string, List<int>> partialDepths = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, SortedSet<int>> entry in depths)
        {
            partialDepths[entry.Key] = bailed.Contains(entry.Key) ? [] : [.. entry.Value];
        }

        return new FormatPlan(partialDepths, bodies);
    }

    private static IEnumerable<int> BasesOf(string path, Dictionary<string, SortedSet<int>> depths)
    {
        yield return 0; // the standalone body is always a call site producer.
        foreach (int depth in depths[path].ToList())
        {
            if (depth != 0)
            {
                yield return depth;
            }
        }
    }

    private static Measure MeasureOf(
        string path,
        IReadOnlyDictionary<string, List<Node>> composedBodies,
        Dictionary<string, Measure> measures,
        HashSet<string> visiting)
    {
        if (measures.TryGetValue(path, out Measure? existing))
        {
            return existing;
        }

        if (!visiting.Add(path))
        {
            // Partial cycle: unformattable, and not memoized so every participant bails.
            return new Measure { Bailed = true, Clean = false };
        }

        Measure measure;
        if (!composedBodies.TryGetValue(path, out List<Node>? body))
        {
            measure = new Measure { Bailed = true, Clean = false };
        }
        else
        {
            // Walk in splice shape (the shape callers embed), building output so the
            // first and last emitted nodes can be inspected. Call depths and the net
            // delta are shape-independent.
            WalkState state = new() { AtLineStart = false };
            List<(PartialNode Node, int Depth)> found = [];
            List<Node> output = [];
            WalkNodes(body, state, output, found, callDepths: [],
                target => MeasureOf(target, composedBodies, measures, visiting));
            StripLeadingWhitespace(output);
            bool lastIsStrippableStatic = output.Count > 0
                && output[output.Count - 1] is StaticNode last
                && last.Text.TrimEnd(' ', '\t').EndsWith("\n", StringComparison.Ordinal);
            measure = new Measure
            {
                Calls = [.. found.Select(f => (f.Node.Path, f.Depth))],
                Delta = state.Depth,
                EndsWithNewline = state.AtLineStart && !lastIsStrippableStatic && output.Count > 0,
                // A template ending with its line start still ambiguous (a
                // conditional partial as the very last node) has no following
                // newline to resolve it; bail rather than guess.
                Bailed = state.Bailed || state.AmbiguousLineStart,
                // An empty splice body (whitespace-only template) is unclean: the
                // caller has already written its call-site indent, so splicing
                // nothing leaves a blank or trailing-space line.
                Clean = !state.Bailed && !state.InTag && !state.InComment
                    && state.Verbatim is null && state.Quote == '\0'
                    && output.Count > 0
                    && !EmitsLeadingNewline(output),
            };
        }

        visiting.Remove(path);
        measures[path] = measure;
        return measure;
    }

    private static void CascadeBails(
        Dictionary<string, Measure> measures,
        Dictionary<string, SortedSet<int>> depths,
        HashSet<string> bailed)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (KeyValuePair<string, Measure> caller in measures)
            {
                if (bailed.Contains(caller.Key))
                {
                    continue;
                }

                foreach ((string target, int relative) in caller.Value.Calls)
                {
                    bool broken = bailed.Contains(target) || !depths.TryGetValue(target, out SortedSet<int>? set)
                        || BasesOf(caller.Key, depths).Any(baseDepth => !set.Contains(baseDepth + relative));
                    if (broken)
                    {
                        bailed.Add(caller.Key);
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    private static bool TryBuildBodies(
        string path,
        List<Node> body,
        SortedSet<int> partialDepths,
        Dictionary<string, Measure> measures,
        HashSet<string> bailed,
        Dictionary<string, FormattedBody> bodies)
    {
        Func<string, Measure> resolve = target =>
            measures.TryGetValue(target, out Measure? m) && !bailed.Contains(target)
                ? m
                : new Measure { Bailed = true, Clean = false };

        WalkState standaloneState = new() { AtLineStart = true };
        List<Node> standaloneOut = [];
        Dictionary<PartialNode, int> standaloneCalls = [];
        WalkNodes(body, standaloneState, standaloneOut, calls: null, standaloneCalls, resolve);
        if (standaloneState.Bailed)
        {
            return false;
        }

        bodies[path + "@S"] = new FormattedBody(standaloneOut, standaloneCalls);

        foreach (int depth in partialDepths)
        {
            WalkState state = new() { Depth = depth, AtLineStart = false };
            List<Node> output = [];
            Dictionary<PartialNode, int> callDepths = [];
            WalkNodes(body, state, output, calls: null, callDepths, resolve);
            if (state.Bailed)
            {
                return false;
            }

            StripLeadingWhitespace(output);
            StripTrailingNewline(output);
            bodies[path + "@" + depth] = new FormattedBody(output, callDepths);
        }

        return true;
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
        public bool AfterEquals;
        public bool InUnquotedValue;
        public string? PendingVerbatim;
        public string? Verbatim;
        public bool InComment;
        public bool Bailed;

        /// <summary>Set when an {{if}}'s branches agreed on everything except
        /// whether the current line's start was consumed (one branch rendered
        /// line content, the other rendered nothing). The walk continues as if
        /// the line was consumed, but ONLY a newline may follow — anything else
        /// would sit at a data-dependent column, so it bails. At runtime the
        /// empty branch yields one blank line, never misindentation.</summary>
        public bool AmbiguousLineStart;

        public WalkState Clone() => (WalkState)MemberwiseClone();

        public void CopyFrom(WalkState other)
        {
            Depth = other.Depth;
            AtLineStart = other.AtLineStart;
            InTag = other.InTag;
            Quote = other.Quote;
            TagEffect = other.TagEffect;
            TagSelfClose = other.TagSelfClose;
            AfterEquals = other.AfterEquals;
            InUnquotedValue = other.InUnquotedValue;
            PendingVerbatim = other.PendingVerbatim;
            Verbatim = other.Verbatim;
            InComment = other.InComment;
            Bailed = other.Bailed;
            AmbiguousLineStart = other.AmbiguousLineStart;
        }

        public bool SameAs(WalkState other) =>
            Depth == other.Depth
            && AtLineStart == other.AtLineStart
            && InTag == other.InTag
            && Quote == other.Quote
            && TagEffect == other.TagEffect
            && TagSelfClose == other.TagSelfClose
            && AfterEquals == other.AfterEquals
            && InUnquotedValue == other.InUnquotedValue
            && PendingVerbatim == other.PendingVerbatim
            && Verbatim == other.Verbatim
            && InComment == other.InComment
            && AmbiguousLineStart == other.AmbiguousLineStart;

        /// <summary>True when the two states differ ONLY in
        /// <see cref="AtLineStart"/> — the shape a conditional partial line
        /// leaves behind (see <see cref="AmbiguousLineStart"/>).</summary>
        public bool SameExceptLineStart(WalkState other) =>
            AtLineStart != other.AtLineStart
            && Depth == other.Depth
            && InTag == other.InTag
            && Quote == other.Quote
            && TagEffect == other.TagEffect
            && TagSelfClose == other.TagSelfClose
            && AfterEquals == other.AfterEquals
            && InUnquotedValue == other.InUnquotedValue
            && PendingVerbatim == other.PendingVerbatim
            && Verbatim == other.Verbatim
            && InComment == other.InComment
            && AmbiguousLineStart == other.AmbiguousLineStart;
    }

    private static void WalkNodes(
        List<Node> nodes,
        WalkState state,
        List<Node>? output,
        List<(PartialNode Node, int Depth)>? calls,
        Dictionary<PartialNode, int>? callDepths,
        Func<string, Measure> resolveCallee)
    {
        foreach (Node node in nodes)
        {
            if (state.Bailed)
            {
                return;
            }

            // An ambiguous line start (conditional partial line) tolerates only
            // a newline next: the line either got content or becomes blank, and
            // both are fine — but any same-line continuation would sit at a
            // data-dependent column, so it bails.
            if (state.AmbiguousLineStart)
            {
                if (node is StaticNode ambiguousNext && ambiguousNext.Text.StartsWith("\n", StringComparison.Ordinal))
                {
                    state.AmbiguousLineStart = false;
                }
                else
                {
                    state.Bailed = true;
                    return;
                }
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
                    WalkNodes(i.Then, thenState, thenOut, calls, callDepths, resolveCallee);
                    WalkState elseState = state.Clone();
                    List<Node>? elseOut = output is null ? null : [];
                    WalkNodes(i.Else, elseState, elseOut, calls, callDepths, resolveCallee);
                    if (thenState.Bailed || elseState.Bailed)
                    {
                        state.Bailed = true;
                        return;
                    }

                    if (thenState.SameAs(elseState))
                    {
                        state.CopyFrom(thenState);
                    }
                    else if (thenState.SameExceptLineStart(elseState))
                    {
                        // One branch consumed the line start, the other rendered
                        // nothing (e.g. a flash banner behind a guard). Continue
                        // as if consumed and let the loop guard above insist on
                        // a newline next.
                        state.CopyFrom(thenState.AtLineStart ? elseState : thenState);
                        state.AmbiguousLineStart = true;
                    }
                    else
                    {
                        state.Bailed = true;
                        return;
                    }
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
                    WalkNodes(f.Body, bodyState, bodyOut, calls, callDepths, resolveCallee);
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

                    // Splice the callee's net effect into this walk: an unclean or
                    // bailed callee makes everything after the call unformattable.
                    Measure callee = resolveCallee(p.Path);
                    if (!callee.Clean)
                    {
                        state.Bailed = true;
                        return;
                    }

                    // Depth is deliberately unclamped here and at '>': a callee that
                    // dips below its own start (`</tr><tr>`) must keep its true net
                    // delta; only rendered indentation clamps, inside Indent().
                    state.Depth += callee.Delta;
                    state.AtLineStart = callee.EndsWithNewline;
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
                        if (state.InUnquotedValue)
                        {
                            output?.Append(c); // a quote inside an unquoted value is a value char.
                            i++;
                            break;
                        }

                        state.Quote = c;
                        state.AfterEquals = false;
                        output?.Append(c);
                        i++;
                        break;
                    case '=':
                        state.TagSelfClose = false;
                        if (!state.InUnquotedValue)
                        {
                            state.AfterEquals = true;
                        }

                        output?.Append(c);
                        i++;
                        break;
                    case '/':
                        // Only a bare '/' marks self-closing: inside an unquoted
                        // attribute value ("<a href=/about/>") it is a value char.
                        if (state.AfterEquals || state.InUnquotedValue)
                        {
                            state.AfterEquals = false;
                            state.InUnquotedValue = true;
                        }
                        else
                        {
                            state.TagSelfClose = true;
                        }

                        output?.Append(c);
                        i++;
                        break;
                    case '>':
                        state.InTag = false;
                        if (!state.TagSelfClose)
                        {
                            state.Depth += state.TagEffect;
                            state.Verbatim = state.PendingVerbatim;
                        }

                        state.TagSelfClose = false;
                        state.TagEffect = 0;
                        state.AfterEquals = false;
                        state.InUnquotedValue = false;
                        state.PendingVerbatim = null;
                        output?.Append(c);
                        i++;
                        break;
                    case '\r':
                        i++;
                        break;
                    case '\n':
                        // Attribute continuation line: one level past the tag's own line.
                        state.InUnquotedValue = false;
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
                            if (state.AfterEquals)
                            {
                                state.AfterEquals = false;
                                state.InUnquotedValue = true;
                            }
                        }
                        else
                        {
                            // Per HTML a '/' not immediately before '>' is ignored,
                            // so whitespace after it means the element still opens.
                            state.TagSelfClose = false;
                            state.InUnquotedValue = false;
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
                    state.AfterEquals = false;
                    state.InUnquotedValue = false;
                    output?.Append(c);
                    i++;
                    continue;
                }

                if (i + 1 < length && text[i + 1] == '/')
                {
                    state.InTag = true;
                    state.TagEffect = -1;
                    state.AfterEquals = false;
                    state.InUnquotedValue = false;
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
                state.AfterEquals = false;
                state.InUnquotedValue = false;
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

    /// <summary>
    /// True when a splice body's first rendered characters would include a newline the
    /// caller's already-written call-site indent cannot absorb: an {{if}}/{{for}} first
    /// node whose own first line starts on a fresh line. Such callees bail their callers.
    /// </summary>
    private static bool EmitsLeadingNewline(List<Node> output)
    {
        if (output.Count == 0)
        {
            return false;
        }

        return output[0] switch
        {
            StaticNode s => s.Text.StartsWith("\n", StringComparison.Ordinal),
            // Both branches must agree inline; a data-dependent line start is unclean.
            IfNode i => EmitsLeadingNewline(i.Then) || EmitsLeadingNewline(i.Else) || i.Then.Count == 0 || i.Else.Count == 0,
            ForNode f => f.Body.Count == 0 || EmitsLeadingNewline(f.Body),
            _ => false, // Expr/Loc/Partial render inline.
        };
    }

    /// <summary>Splice bodies start where the caller's indent left off: directive
    /// residue newlines and indentation before the first visible content are dropped.</summary>
    private static void StripLeadingWhitespace(List<Node> output)
    {
        while (output.Count > 0 && output[0] is StaticNode first)
        {
            string text = first.Text.TrimStart(' ', '\t', '\r', '\n');
            if (text.Length == 0)
            {
                output.RemoveAt(0);
                continue;
            }

            if (text.Length != first.Text.Length)
            {
                output[0] = new StaticNode(text);
            }

            break;
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
