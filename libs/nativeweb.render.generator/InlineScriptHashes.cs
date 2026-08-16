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
/// hashing the wrong bytes costs the same thing in production instead.
///
/// A body carrying dynamic output is unhashable by construction rather than by
/// the guards below — a hash is only ever taken over bytes lifted verbatim from
/// a StaticNode. That is NOT the same as being unable to over-authorize, and
/// this file used to claim it was. Static bytes can spell '&lt;script&gt;' in a
/// place a browser reads as text: inside a comment, inside a raw-text or RCDATA
/// element, or inside another tag's attribute value. Every one of those used to
/// be hashed, putting a 'script-src' entry on the page for a script the page
/// does not ship — and the commented-out case is the one that happens, because
/// commenting a script out during debugging is normal and leaving its bytes
/// authorized afterwards is not. Phase.Outside therefore walks tags rather than
/// searching for a literal, and carries the three states that decide whether
/// '&lt;script' is a tag at all.
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
    private const string CloseTag = "</script";

    /// <summary>
    /// Elements whose content a browser tokenizes as text, not as markup, so a
    /// '&lt;script&gt;' inside one is characters on the page rather than a
    /// script element. Raw text ('style'), RCDATA ('textarea', 'title') and the
    /// legacy raw-text shapes are all one case here, because the scan only ever
    /// wants to skip to the close tag. 'noscript' is included: with scripting
    /// enabled, which is the only case where CSP matters, its content is raw
    /// text too. 'plaintext' never ends, and staying in the state forever is the
    /// correct reading of it.
    /// </summary>
    private static readonly HashSet<string> RawTextElements = new(StringComparer.Ordinal)
    {
        "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes", "noscript", "plaintext",
    };

    /// <summary>Where the scan sits relative to one script element.</summary>
    private enum Phase
    {
        Outside,
        InOpenTag,
        InBody,
    }

    /// <summary>
    /// Where the scan sits when it is not inside a script element — which is
    /// what decides whether the next '&lt;script' is a tag or is text.
    /// </summary>
    private enum OutsideState
    {
        /// <summary>In content, where a '&lt;' can open a tag.</summary>
        Normal,

        /// <summary>Between '&lt;!--' and '--&gt;'.</summary>
        InComment,

        /// <summary>Inside a raw-text or RCDATA element's content.</summary>
        InRawText,

        /// <summary>Inside some other element's open tag, past its name.</summary>
        InTag,
    }

    /// <summary>Scanner state, carried across node boundaries: an open tag's
    /// attributes and a body may both be split by dynamic nodes.</summary>
    private sealed class ScanState
    {
        public Phase Phase;

        /// <summary>Only meaningful while <see cref="Phase"/> is Outside.</summary>
        public OutsideState Outside;

        /// <summary>The raw-text element whose close tag is being looked for.</summary>
        public string RawTextTag = string.Empty;

        /// <summary>
        /// Set while skipping the open tag of a raw-text element: its content
        /// becomes raw text only once that tag's '&gt;' has been passed.
        /// </summary>
        public string? PendingRawText;

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
                case IfNode i when state.Phase == Phase.Outside && state.Outside == OutsideState.Normal:
                    // A script must open and close inside one branch: the fresh
                    // state drops anything still open at the block's edge.
                    Scan(i.Then, new ScanState(), hashes);
                    Scan(i.Else, new ScanState(), hashes);
                    break;
                case ForNode f when state.Phase == Phase.Outside && state.Outside == OutsideState.Normal:
                    Scan(f.Body, new ScanState(), hashes);
                    break;
                case IfNode or ForNode when state.Phase == Phase.Outside:
                    // Outside a script, but inside a comment, a raw-text element or
                    // another tag. Whatever the branch spells is not a script element
                    // here, and rescanning it with a fresh state is exactly how a
                    // commented-out script gets authorized. Skipped whole, so any
                    // '-->' or close tag inside it goes unseen and the state persists:
                    // that can suppress a later hash, and can never invent one.
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
                i = ScanOutside(text, i, state);
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
    /// Advances through content that is not inside a script element, and returns
    /// where to carry on from. This is the whole of the context awareness: it
    /// decides whether the next '&lt;script' is a tag or is text, and it is the
    /// only thing standing between a commented-out script and a CSP entry
    /// authorizing it.
    /// </summary>
    /// <remarks>
    /// Deliberately not an HTML tokenizer. It recognises the three places where
    /// '&lt;script' is not a tag and skips them; everything else it does not
    /// understand it treats as content, which can only ever cost a hash, never
    /// add one. Every state is carried on <see cref="ScanState"/> because a
    /// comment, a raw-text body and an open tag can each be split across nodes
    /// by a dynamic expression.
    /// </remarks>
    private static int ScanOutside(string text, int i, ScanState state)
    {
        switch (state.Outside)
        {
            case OutsideState.InComment:
            {
                int end = text.IndexOf("-->", i, StringComparison.Ordinal);
                if (end < 0)
                {
                    return text.Length;
                }

                state.Outside = OutsideState.Normal;
                return end + 3;
            }

            case OutsideState.InRawText:
            {
                int close = IndexOfTag(text, "</" + state.RawTextTag, i);
                if (close < 0)
                {
                    return text.Length;
                }

                state.Outside = OutsideState.Normal;
                return close + 2 + state.RawTextTag.Length;
            }

            case OutsideState.InTag:
                return SkipTagRemainder(text, i, state);
        }

        int lt = text.IndexOf('<', i);
        if (lt < 0)
        {
            return text.Length;
        }

        if (string.CompareOrdinal(text, lt, "<!--", 0, 4) == 0)
        {
            state.Outside = OutsideState.InComment;
            return lt + 4;
        }

        // '<!DOCTYPE', a processing instruction, or a bogus comment: all end at
        // the next '>' and none of them can contain a tag.
        if (lt + 1 < text.Length && text[lt + 1] is '!' or '?')
        {
            int gt = text.IndexOf('>', lt);
            return gt < 0 ? text.Length : gt + 1;
        }

        int nameStart = lt + 1;
        bool closing = nameStart < text.Length && text[nameStart] == '/';
        if (closing)
        {
            nameStart++;
        }

        int nameEnd = nameStart;
        while (nameEnd < text.Length && IsNameChar(text[nameEnd]))
        {
            nameEnd++;
        }

        // A '<' that starts no tag name is just a character. It cannot be a tag,
        // so step over it rather than treating the rest of the node as markup.
        if (nameEnd == nameStart)
        {
            return lt + 1;
        }

        string name = Lowercase(text, nameStart, nameEnd - nameStart);

        if (!closing && name == "script")
        {
            state.Phase = Phase.InOpenTag;
            state.Quote = '\0';
            state.LastSignificant = '\0';
            state.OpenTag.Length = 0;
            return nameEnd;
        }

        state.Outside = OutsideState.InTag;
        state.Quote = '\0';
        state.LastSignificant = '\0';

        // The content becomes raw text only once this open tag is closed; a
        // close tag of the same name obviously starts nothing.
        state.PendingRawText = !closing && RawTextElements.Contains(name) ? name : null;

        return SkipTagRemainder(text, nameEnd, state);
    }

    /// <summary>
    /// Skips from inside an open tag to just past its '&gt;', tracking quoted
    /// attribute values so that a '&gt;' — or a '&lt;script' — inside one is not
    /// mistaken for markup.
    /// </summary>
    private static int SkipTagRemainder(string text, int i, ScanState state)
    {
        while (i < text.Length)
        {
            char c = text[i++];
            if (state.Quote != '\0')
            {
                if (c == state.Quote)
                {
                    state.Quote = '\0';
                }

                continue;
            }

            // Same rule as the script open tag below: a quote opens a value only
            // straight after '=', so data-note=don't does not swallow the tag.
            if (c is '"' or '\'' && state.LastSignificant == '=')
            {
                state.Quote = c;
            }
            else if (c == '>')
            {
                if (state.PendingRawText is not null)
                {
                    state.Outside = OutsideState.InRawText;
                    state.RawTextTag = state.PendingRawText;
                    state.PendingRawText = null;
                }
                else
                {
                    state.Outside = OutsideState.Normal;
                }

                state.LastSignificant = '\0';
                return i;
            }

            if (!char.IsWhiteSpace(c))
            {
                state.LastSignificant = c;
            }
        }

        return text.Length;
    }

    /// <summary>Tag-name characters, as far as this scan needs to tell them apart.</summary>
    private static bool IsNameChar(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-';

    private static string Lowercase(string text, int start, int length)
    {
        var name = new StringBuilder(length);
        for (int i = start; i < start + length; i++)
        {
            name.Append(char.ToLowerInvariant(text[i]));
        }

        return name.ToString();
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
            || JavaScriptMimeTypes.Contains(type);
    }

    /// <summary>
    /// The HTML standard's "JavaScript MIME type" set, verbatim. A browser
    /// evaluates a classic script whose type essence-matches any of these, so
    /// every one of them needs a hash on a nonce-free page.
    /// </summary>
    /// <remarks>
    /// The legacy spellings are not decoration. They are the failure direction
    /// that costs something: an unrecognised-but-executing type means no hash,
    /// which means a script the browser runs everywhere except under this CSP —
    /// blocked in production with every test green. Recognising one needlessly
    /// would only add an allowlist entry for bytes the page already ships.
    /// </remarks>
    private static readonly HashSet<string> JavaScriptMimeTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/ecmascript",
            "application/javascript",
            "application/x-ecmascript",
            "application/x-javascript",
            "text/ecmascript",
            "text/javascript",
            "text/javascript1.0",
            "text/javascript1.1",
            "text/javascript1.2",
            "text/javascript1.3",
            "text/javascript1.4",
            "text/javascript1.5",
            "text/jscript",
            "text/livescript",
            "text/x-ecmascript",
            "text/x-javascript",
        };

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
