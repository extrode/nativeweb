using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace NativeWeb.Sample.Render;

/// <summary>
/// Renders <c>GET /articles</c>: the published feed, as one static HTML document.
/// </summary>
/// <remarks>
/// <para>
/// A hand-written renderer, mirroring <c>apps/web/Render/</c>. There is no view
/// engine here and no template file: a page is a static method that returns a
/// string, and the type system is what tells you a field is missing. That is the
/// same trade the whole architecture makes — see
/// <c>docs/native-web-architecture.md</c>.
/// </para>
/// <para>
/// **Every value goes through <see cref="HtmlEncoder"/>.** The encoder is called
/// explicitly rather than by a template engine's default, so the one place raw
/// markup could enter is visible by grep. Nothing on this page is trusted: the
/// title and author come from a database that an admin form writes to.
/// </para>
/// </remarks>
internal static class ArticlesPage
{
    public static string Render(IReadOnlyList<PublishedArticle> articles)
    {
        HtmlEncoder encoder = HtmlEncoder.Default;

        var items = new StringBuilder();
        if (articles.Count == 0)
        {
            // An empty feed is a normal state, not an error: a fresh database has
            // no published rows. Saying so beats rendering an empty <ul>, which
            // reads as a broken page.
            items.Append("<p class=\"articles__empty\">No published articles yet.</p>");
        }
        else
        {
            items.Append("<ul class=\"articles\">");
            foreach (PublishedArticle article in articles)
            {
                string title = encoder.Encode(article.Title);
                string date = article.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                // The author is the one nullable field. Rendered as a separate
                // element rather than an empty one, so an author-less article has
                // no stray punctuation.
                string author = article.Author is { Length: > 0 } name
                    ? $"<span class=\"articles__author\">{encoder.Encode(name)}</span>"
                    : "";

                items.Append(CultureInfo.InvariantCulture, $"""
                    <li class="articles__item">
                      <h2 class="articles__title">{title}</h2>
                      {author}
                      <time class="articles__date" datetime="{date}">{date}</time>
                    </li>
                    """);
            }

            items.Append("</ul>");
        }

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Articles</title>
            </head>
            <body>
              <main>
                <h1>Articles</h1>
                {items}
              </main>
            </body>
            </html>
            """;
    }
}
