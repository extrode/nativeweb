using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NativeWeb.Render.Generator;

/// <summary>
/// CSP hash sources for a template's static inline scripts (spec 006 R3). Each
/// hash is taken over the exact bytes the generated Render writes between a
/// script's open tag and its '&lt;/script&gt;' — the formatter keeps verbatim
/// bodies byte-for-byte, so what is hashed here is what the browser hashes.
/// Attributes are irrelevant (the nonce attribute may well be dynamic); a body
/// containing any dynamic node cannot be hash-authorized and is omitted, no
/// diagnostic: such a page is not a hash-authorization candidate in the first
/// place.
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
        public char Quote;
        public bool Dynamic;
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
                    // mid-script) are dynamic: inside a body they disqualify it,
                    // inside the open tag they are just an attribute value.
                    if (state.Phase == Phase.InBody)
                    {
                        state.Dynamic = true;
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
                int open = IndexOfOpenTag(text, i);
                if (open < 0)
                {
                    return;
                }

                state.Phase = Phase.InOpenTag;
                state.Quote = '\0';
                i = open + OpenTag.Length;
                continue;
            }

            if (state.Phase == Phase.InOpenTag)
            {
                char c = text[i++];
                if (state.Quote != '\0')
                {
                    if (c == state.Quote)
                    {
                        state.Quote = '\0';
                    }
                }
                else if (c is '"' or '\'')
                {
                    state.Quote = c;
                }
                else if (c == '>')
                {
                    state.Phase = Phase.InBody;
                    state.Dynamic = false;
                    state.Body.Length = 0;
                }

                continue;
            }

            // In the body: everything up to the close tag is hashed as authored.
            int close = IndexOfIgnoreCase(text, CloseTag, i);
            if (close < 0)
            {
                state.Body.Append(text, i, text.Length - i);
                return;
            }

            state.Body.Append(text, i, close - i);
            // An empty body is a src= script — there is nothing to authorize.
            if (!state.Dynamic && state.Body.Length > 0)
            {
                hashes.Add(HashSource(state.Body.ToString()));
            }

            state.Phase = Phase.Outside;
            i = close + CloseTag.Length;
        }
    }

    /// <summary>Finds the next '&lt;script' whose name really ends there.</summary>
    private static int IndexOfOpenTag(string text, int start)
    {
        int at = start;
        while ((at = IndexOfIgnoreCase(text, OpenTag, at)) >= 0)
        {
            int after = at + OpenTag.Length;
            if (after >= text.Length || text[after] is ' ' or '\t' or '\r' or '\n' or '>' or '/')
            {
                return at;
            }

            at = after;
        }

        return -1;
    }

    private static string HashSource(string body)
    {
        using var sha256 = SHA256.Create();
        return "sha256-" + Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static int IndexOfIgnoreCase(string text, string value, int start) =>
        text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
}
