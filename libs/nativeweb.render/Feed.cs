using System.Globalization;
using System.Xml;

namespace NativeWeb.Render;

/// <summary>
/// A feed as both Atom and RSS see it (spec 021 R4): the same document goes to
/// <see cref="FormatResults.Atom(Feed, Uri)"/> and <see cref="FormatResults.Rss(Feed, Uri)"/>.
/// </summary>
/// <remarks>
/// Links are paths or absolute URIs and are resolved against the base the helper
/// is given, so a feed never carries a relative link. <paramref name="Author"/> is
/// what Atom requires when an entry has none of its own; a feed whose entries all
/// name an author may leave it null.
/// </remarks>
/// <param name="Id">A permanent identifier, usually the feed's own absolute URL.</param>
/// <param name="Title">The feed's title.</param>
/// <param name="Link">The page the feed is about, a path or an absolute URI.</param>
/// <param name="Updated">When the feed last changed.</param>
/// <param name="Entries">The entries, newest first by convention.</param>
/// <param name="Author">The feed-level author, when entries do not carry one.</param>
/// <param name="Subtitle">A one-line description; RSS requires one and falls back to the title.</param>
public sealed record Feed(
    string Id,
    string Title,
    string Link,
    DateTimeOffset Updated,
    IReadOnlyList<FeedEntry> Entries,
    string? Author = null,
    string? Subtitle = null);

/// <summary>One feed entry.</summary>
/// <param name="Id">A permanent identifier, usually the entry's absolute URL.</param>
/// <param name="Title">The entry's title.</param>
/// <param name="Link">The entry's page, a path or an absolute URI.</param>
/// <param name="Updated">When the entry last changed.</param>
/// <param name="Summary">A short plain-text summary, or null.</param>
/// <param name="Author">The entry's author, or null to inherit the feed's.</param>
public sealed record FeedEntry(
    string Id,
    string Title,
    string Link,
    DateTimeOffset Updated,
    string? Summary = null,
    string? Author = null);

/// <summary>How often a sitemap entry is expected to change.</summary>
public enum ChangeFrequency
{
    /// <summary>Changes on every access.</summary>
    Always,

    /// <summary>Hourly.</summary>
    Hourly,

    /// <summary>Daily.</summary>
    Daily,

    /// <summary>Weekly.</summary>
    Weekly,

    /// <summary>Monthly.</summary>
    Monthly,

    /// <summary>Yearly.</summary>
    Yearly,

    /// <summary>Archived; never changes.</summary>
    Never,
}

/// <summary>One sitemap entry (spec 021 R4).</summary>
/// <param name="Location">The page, a path or an absolute URI.</param>
/// <param name="LastModified">When it last changed, or null to omit.</param>
/// <param name="Frequency">How often it changes, or null to omit.</param>
/// <param name="Priority">Its priority relative to the site's other pages, 0.0 to 1.0, or null to omit.</param>
public sealed record SitemapEntry(
    string Location,
    DateTimeOffset? LastModified = null,
    ChangeFrequency? Frequency = null,
    double? Priority = null);

/// <summary>
/// The Atom 1.0, RSS 2.0 and sitemaps.org writers behind <see cref="FormatResults"/>.
/// Everything goes through <see cref="XmlWriter"/>'s string methods, so no text
/// from a caller reaches the document unescaped.
/// </summary>
internal static class FeedWriter
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public static void Atom(XmlWriter w, Feed feed, Uri baseUrl)
    {
        w.WriteStartElement("feed", AtomNamespace);
        w.WriteElementString("id", AtomNamespace, feed.Id);
        w.WriteElementString("title", AtomNamespace, feed.Title);
        w.WriteElementString("updated", AtomNamespace, Rfc3339(feed.Updated));
        AtomLink(w, "alternate", Resolve(baseUrl, feed.Link));

        if (feed.Subtitle is { Length: > 0 })
        {
            w.WriteElementString("subtitle", AtomNamespace, feed.Subtitle);
        }

        if (feed.Author is { Length: > 0 })
        {
            AtomAuthor(w, feed.Author);
        }

        foreach (FeedEntry entry in feed.Entries)
        {
            w.WriteStartElement("entry", AtomNamespace);
            w.WriteElementString("id", AtomNamespace, entry.Id);
            w.WriteElementString("title", AtomNamespace, entry.Title);
            w.WriteElementString("updated", AtomNamespace, Rfc3339(entry.Updated));
            AtomLink(w, "alternate", Resolve(baseUrl, entry.Link));

            if (entry.Summary is { Length: > 0 })
            {
                w.WriteElementString("summary", AtomNamespace, entry.Summary);
            }

            if (entry.Author is { Length: > 0 })
            {
                AtomAuthor(w, entry.Author);
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    public static void Rss(XmlWriter w, Feed feed, Uri baseUrl)
    {
        w.WriteStartElement("rss");
        w.WriteAttributeString("version", "2.0");
        w.WriteAttributeString("xmlns", "dc", null, DublinCoreNamespace);
        w.WriteStartElement("channel");
        w.WriteElementString("title", feed.Title);
        w.WriteElementString("link", Resolve(baseUrl, feed.Link));
        w.WriteElementString("description", feed.Subtitle is { Length: > 0 } ? feed.Subtitle : feed.Title);
        w.WriteElementString("lastBuildDate", Rfc822(feed.Updated));

        if (feed.Author is { Length: > 0 })
        {
            w.WriteElementString("creator", DublinCoreNamespace, feed.Author);
        }

        foreach (FeedEntry entry in feed.Entries)
        {
            w.WriteStartElement("item");
            w.WriteElementString("title", entry.Title);
            w.WriteElementString("link", Resolve(baseUrl, entry.Link));
            w.WriteStartElement("guid");
            w.WriteAttributeString("isPermaLink", "false");
            w.WriteString(entry.Id);
            w.WriteEndElement();
            w.WriteElementString("pubDate", Rfc822(entry.Updated));

            if (entry.Summary is { Length: > 0 })
            {
                w.WriteElementString("description", entry.Summary);
            }

            if (entry.Author is { Length: > 0 })
            {
                w.WriteElementString("creator", DublinCoreNamespace, entry.Author);
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndElement();
    }

    public static void Sitemap(XmlWriter w, IEnumerable<SitemapEntry> entries, Uri baseUrl)
    {
        w.WriteStartElement("urlset", SitemapNamespace);

        foreach (SitemapEntry entry in entries)
        {
            if (entry.Priority is < 0.0 or > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entries),
                    entry.Priority,
                    $"A sitemap priority is between 0.0 and 1.0; {entry.Location} has {entry.Priority}.");
            }

            w.WriteStartElement("url", SitemapNamespace);
            w.WriteElementString("loc", SitemapNamespace, Resolve(baseUrl, entry.Location));

            if (entry.LastModified is { } modified)
            {
                w.WriteElementString("lastmod", SitemapNamespace, Rfc3339(modified));
            }

            if (entry.Frequency is { } frequency)
            {
                w.WriteElementString(
                    "changefreq",
                    SitemapNamespace,
                    frequency.ToString().ToLowerInvariant());
            }

            if (entry.Priority is { } priority)
            {
                w.WriteElementString(
                    "priority",
                    SitemapNamespace,
                    priority.ToString("0.0", CultureInfo.InvariantCulture));
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    internal static string Resolve(Uri baseUrl, string link) => new Uri(baseUrl, link).AbsoluteUri;

    internal static string Rfc3339(DateTimeOffset when) =>
        when.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static string Rfc822(DateTimeOffset when) =>
        when.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);

    private static void AtomLink(XmlWriter w, string rel, string href)
    {
        w.WriteStartElement("link", AtomNamespace);
        w.WriteAttributeString("rel", rel);
        w.WriteAttributeString("href", href);
        w.WriteEndElement();
    }

    private static void AtomAuthor(XmlWriter w, string name)
    {
        w.WriteStartElement("author", AtomNamespace);
        w.WriteElementString("name", AtomNamespace, name);
        w.WriteEndElement();
    }
}
