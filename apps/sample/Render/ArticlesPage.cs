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
/// <c>docs/01-architecture/native-web-architecture.md</c>.
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
    /// <param name="articles">What to show.</param>
    /// <param name="query">The filter text that produced this list, echoed back into
    /// the box so a filtered page does not look like an unfiltered one.</param>
    /// <param name="token">The antiforgery request token. Null omits the form
    /// entirely rather than rendering one that would be rejected on submit.</param>
    public static string Render(
        IReadOnlyList<PublishedArticle> articles,
        string? query = null,
        string? token = null)
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

        // The counterpart to ArticleFilter: the wire names in here are the ones the
        // generated binder reads, and nothing else on the form can reach the handler.
        // Omitted without a token, because a form that cannot be submitted is worse
        // than no form.
        string filter = token is null
            ? ""
            : $"""
                <form class="articles__filter" method="post" action="/articles">
                  <input type="hidden" name="__RequestVerificationToken" value="{encoder.Encode(token)}">
                  <div class="field">
                    <label class="field__label" for="q">Filter</label>
                    <input class="input" id="q" name="query" type="search"
                           placeholder="Title or author" value="{encoder.Encode(query ?? "")}">
                  </div>
                  <div class="field">
                    <label class="field__label" for="max">Max</label>
                    <input class="input" id="max" name="max" type="number" min="1" max="100" value="20">
                  </div>
                  <button class="btn btn--primary" type="submit">Filter</button>
                </form>
                """;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Articles</title>
              <script src="/js/theme.js"></script>
              <link rel="stylesheet" href="/css/tokens.css">
              <link rel="stylesheet" href="/css/base.css">
              <link rel="stylesheet" href="/css/components.css">
              <link rel="stylesheet" href="/css/sample.css">
              <link rel="stylesheet" href="/vendor/fonts/fonts.css">
              <script src="/vendor/nativeweb/boot.js" defer></script>
              <script src="/vendor/htmx/htmx.min.js" defer></script>
              <script src="/vendor/alpinejs/alpine.min.js" defer></script>
            </head>
            <body>
              <header class="topbar">
                <a class="topbar__brand" href="/articles">NativeWeb sample</a>
                <nav class="topbar__nav">
                  <a href="/articles">Articles</a>
                  <a href="/admin">Admin</a>
                </nav>
              </header>
              <main class="page" id="main">
                <h1 class="page__title">Articles</h1>
                <p class="page__lede">
                  Rendered server-side by apps/sample. The filter posts over htmx and
                  falls back to a full-page POST with JavaScript off.
                </p>
                {filter}
                {items}
              </main>
              <footer class="foot">apps/sample -- the NativeWeb reference application</footer>
            </body>
            </html>
            """;
    }
}
