using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NativeWeb.Render.Generator;

/// <summary>
/// CSP hash sources for a template's static inline scripts (spec 006 R3). Each
/// hash is taken over the exact bytes the generated Render writes between a
/// script's open tag and the '&lt;/script&gt;' that really ends it — the
/// formatter keeps verbatim bodies byte-for-byte, so what is hashed here is
/// what the browser hashes. Attributes are excluded (the nonce attribute may
/// well be dynamic), which is what lets a page drop its nonce and still
/// authorize its scripts.
///
/// The scanner is deliberately conservative: every shape it is not certain it
/// frames the same way a browser would is dropped rather than hashed. Dropping
/// costs a blocked script, which is a visibly broken page in the test suite;
/// hashing the wrong bytes costs the same thing in production instead. Nothing
/// here can over-authorize — a hash is only ever computed over bytes taken
/// verbatim from a StaticNode, so a body carrying any dynamic output is
/// unhashable by construction, not merely by the guard below.
///
/// **Not covered: a script inside a partial.** Collect walks the page's own
/// nodes, and a partial's bytes are written by a separate RenderAtD method
/// whose indentation depends on the call depth. A partial-borne script is
/// therefore not hashed and would be blocked under a hash-only CSP. The
/// backstop is the consuming app's lockstep test, which recomputes hashes from
/// the actually-served response rather than from this scan.
/// </summary>
internal static class InlineScriptHashes
{
    private const string OpenTag = "<script";
    private const string CloseTag = "</script";

    /// <summary>Where the scan sits relative to one script element.</summary>
    private enum Phase
    {
        Outside,
        InOpenTag,
        InBody,
    }

    /// <summary>Scanner state, carried across node boundaries: an open tag's
    /// attributes and a body may both be split by dynamic nodes.</summary>
    private sealed class ScanState
    {
        public Phase Phase;

        /// <summary>The quote character of the attribute value being read, else '\0'.</summary>
        public char Quote;

        /// <summary>Last non-whitespace char of the open tag: '=' means a quote here opens a value.</summary>
        public char LastSignificant;

        /// <summary>Set when the body cannot be authorized by a build-time hash.</summary>
        public bool Unhashable;

        public StringBuilder OpenTag = new();
        public StringBuilder Body = new();
    }

    /// <summary>The 'sha256-…' sources for one emitted body, in document order.</summary>
    public static List<string> Collect(List<Node> nodes)
    {
        List<string> hashes = [];
        Scan(nodes, new ScanState(), hashes);
        return hashes;
    }

    private static void Scan(List<Node> nodes, ScanState state, List<string> hashes)
    {
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case StaticNode s:
                    ScanStatic(s.Text, state, hashes);
                    break;
                case IfNode i when state.Phase == Phase.Outside:
                    // A script must open and close inside one branch: the fresh
                    // state drops anything still open at the block's edge.
                    Scan(i.Then, new ScanState(), hashes);
                    Scan(i.Else, new ScanState(), hashes);
                    break;
                case ForNode f when state.Phase == Phase.Outside:
                    Scan(f.Body, new ScanState(), hashes);
                    break;
                default:
                    // Expressions, locale lookups, partial calls (and blocks met
                    // mid-script) are dynamic. Inside a body that disqualifies
                    // it. Inside the open tag it is an attribute value, which is
                    // not hashed — but it also means any 'type' attribute may be
                    // the dynamic part, so a body that turns out to be non-JS
                    // cannot be recognised; see IsExecutableScript.
                    if (state.Phase == Phase.InBody)
                    {
                        state.Unhashable = true;
                    }

                    break;
            }
        }
    }

    private static void ScanStatic(string text, ScanState state, List<string> hashes)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (state.Phase == Phase.Outside)
            {
                int open = IndexOfTag(text, OpenTag, i);
                if (open < 0)
                {
                    return;
                }

                state.Phase = Phase.InOpenTag;
                state.Quote = '\0';
                state.LastSignificant = '\0';
                state.OpenTag.Length = 0;
                i = open + OpenTag.Length;
                continue;
            }

            if (state.Phase == Phase.InOpenTag)
            {
                char c = text[i++];
                state.OpenTag.Append(c);
                if (state.Quote != '\0')
                {
                    if (c == state.Quote)
                    {
                        state.Quote = '\0';
                    }

                    continue;
                }

                // A quote only opens a value straight after '='. Elsewhere in a
                // tag it is an ordinary character, exactly as HTML's unquoted
                // attribute value state has it — treating the apostrophe in
                // data-note=don't as a quote would swallow the tag's '>' and
                // frame the body from the wrong place.
                if (c is '"' or '\'' && state.LastSignificant == '=')
                {
                    state.Quote = c;
                }
                else if (c == '>')
                {
                    state.Phase = Phase.InBody;
                    state.Unhashable = false;
                    state.Body.Length = 0;
                    continue;
                }

                if (!char.IsWhiteSpace(c))
                {
                    state.LastSignificant = c;
                }

                continue;
            }

            // In the body: everything up to the close tag is hashed as authored.
            int close = IndexOfTag(text, CloseTag, i);
            if (close < 0)
            {
                state.Body.Append(text, i, text.Length - i);
                return;
            }

            state.Body.Append(text, i, close - i);
            string body = state.Body.ToString();

            // An empty body is a src= script — there is nothing to authorize.
            // '<!--' puts the browser in script-data-escaped state, where the
            // element does not end at the next '</script>'; rather than model
            // that state, refuse to frame such a body at all.
            if (!state.Unhashable
                && body.Length > 0
                && body.IndexOf("<!--", StringComparison.Ordinal) < 0
                && IsExecutableScript(state.OpenTag.ToString()))
            {
                hashes.Add(HashSource(body));
            }

            state.Phase = Phase.Outside;
            i = close + CloseTag.Length;
        }
    }

    /// <summary>
    /// Finds the next '&lt;script' or '&lt;/script' whose tag name really ends
    /// there. Without the terminator check, '&lt;/scripting' inside a string
    /// literal would end the element early and the hash would cover a truncated
    /// body — a script blocked in production and nowhere else.
    /// </summary>
    private static int IndexOfTag(string text, string tag, int start)
    {
        int at = start;
        while ((at = text.IndexOf(tag, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int after = at + tag.Length;
            if (after >= text.Length || text[after] is ' ' or '\t' or '\r' or '\n' or '\f' or '>' or '/')
            {
                return at;
            }

            at = after;
        }

        return -1;
    }

    /// <summary>
    /// Whether the open tag's attributes mark this as executable script. A
    /// 'type' naming a data format (application/json, text/template) means the
    /// browser never evaluates the body, so hashing it would add an allowlist
    /// entry authorizing that exact string as script everywhere on the page,
    /// for no benefit. An absent or dynamic type reads as executable, which is
    /// the safe default here: the cost of a needless entry is bounded, the cost
    /// of a missing one is a blocked script.
    /// </summary>
    private static bool IsExecutableScript(string openTag)
    {
        string? type = AttributeValue(openTag, "type");
        if (type is null)
        {
            return true;
        }

        type = type.Trim();
        int semicolon = type.IndexOf(';');
        if (semicolon >= 0)
        {
            type = type.Substring(0, semicolon).Trim();
        }

        return type.Length == 0
            || type.Equals("module", StringComparison.OrdinalIgnoreCase)
            || type.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("text/ecmascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/ecmascript", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The quoted value of a named attribute in an open tag's text, or null when
    /// it is absent. Only the quoted form is recognised; an unquoted or dynamic
    /// value reads as absent, which IsExecutableScript treats as executable.
    /// </summary>
    private static string? AttributeValue(string openTag, string name)
    {
        int at = 0;
        while ((at = openTag.IndexOf(name, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int before = at - 1;
            int after = at + name.Length;
            bool standalone = (before < 0 || char.IsWhiteSpace(openTag[before]))
                && after < openTag.Length;
            at = after;
            if (!standalone)
            {
                continue;
            }

            int j = after;
            while (j < openTag.Length && char.IsWhiteSpace(openTag[j]))
            {
                j++;
            }

            if (j >= openTag.Length || openTag[j] != '=')
            {
                continue;
            }

            j++;
            while (j < openTag.Length && char.IsWhiteSpace(openTag[j]))
            {
                j++;
            }

            if (j >= openTag.Length || openTag[j] is not ('"' or '\''))
            {
                continue;
            }

            char quote = openTag[j];
            int end = openTag.IndexOf(quote, j + 1);
            if (end < 0)
            {
                return null;
            }

            return openTag.Substring(j + 1, end - j - 1);
        }

        return null;
    }

    /// <summary>
    /// Hashes the body the way the browser will, which is not byte-for-byte the
    /// way it was authored: the HTML input stream is newline-normalized before
    /// tokenizing, so CRLF and a lone CR both become LF in the text node CSP
    /// actually hashes. Hashing the authored bytes instead makes the constant
    /// depend on the line endings the template happened to be checked out with
    /// — a CRLF working tree and an LF CI checkout of the same commit emit
    /// different hashes, and the CRLF one blocks its own script in every
    /// browser. Normalizing only the hash input is enough; the served bytes can
    /// stay as they are because the browser normalizes them for us.
    /// </summary>
    private static string HashSource(string body)
    {
        string normalized = body.IndexOf('\r') < 0
            ? body
            : body.Replace("\r\n", "\n").Replace('\r', '\n');

        using var sha256 = SHA256.Create();
        return "sha256-" + Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
    }
}
