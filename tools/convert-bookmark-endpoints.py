# One-shot conversion helper: rewrites BookmarkEndpoints.cs from the old
# string-builder pages to the generated templates. Part of the R42 scripted
# conversion; safe to delete after V4 merges.
import sys

p = 'apps/devmarks/Bookmarks/BookmarkEndpoints.cs'
s = open(p, encoding='utf-8').read()
orig = s


def sub(old, new):
    global s
    if old not in s:
        print('MISS:', old[:80].replace('\n', '\\n'))
        return
    s = s.replace(old, new)


sub("""using NativeWeb.DevMarks.Render;
""", """using NativeWeb.DevMarks.Render;
using NativeWeb.Render;

using BookmarkDelete = NativeWeb.DevMarks.Templates.Bookmarks.Delete;
using BookmarkForm = NativeWeb.DevMarks.Templates.Bookmarks.Form;
using BookmarksIndex = NativeWeb.DevMarks.Templates.Bookmarks.Index;
""")

for method in ['ListPageAsync(PageSize, offset, userId, ct)',
               'ListPageByTagAsync(PageSize, offset, userId, tag!, ct)',
               'SearchPageAsync(PageSize, offset, userId, q, ct)',
               'SearchPageByTagAsync(PageSize, offset, userId, q, tag, ct)']:
    sub("""(await db.Bookmarks.%s.ConfigureAwait(false))
                .Select(r => new BookmarksPage.Row(
                    r.Id, r.Url, r.Title, r.Description, r.FaviconContentType is not null, r.IsPublic, r.CreatedAt, SplitTags(r.Tags)))
                .ToList();""" % method,
        """(await db.Bookmarks.%s.ConfigureAwait(false))
                .Select(r => BookmarkRow.Create(
                    r.Id, r.Url, r.Title, r.Description, r.FaviconContentType is not null, r.IsPublic, r.CreatedAt, SplitTags(r.Tags), Hrefs.Tag))
                .ToList();""" % method)

sub("""        List<BookmarksPage.Row> viewRows;""", """        List<BookmarkRow> viewRows;""")

sub("""        return Results.Content(BookmarksPage.Render(context, assets, antiforgery, viewRows, current, totalPages, total, tag, q), Html);
    }""", """        Chrome chrome = Chrome.For(context, assets, antiforgery);
        LocaleCatalog locale = chrome.Locale;

        string heading = (q, tag) switch
        {
            (not null, not null) => locale.Format("bookmarks.heading_q_tag", ("q", q), ("tag", tag)),
            (not null, null) => locale.Format("bookmarks.heading_q", ("q", q)),
            (null, not null) => locale.Format("bookmarks.heading_tag", ("tag", tag)),
            _ => locale["bookmarks.heading"],
        };

        // "saved" counts a collection; a search counts what it found.
        string countText = q is null
            ? locale.Format("bookmarks.count_saved", ("count", Invariant(total)))
            : total == 1 ? locale["count.result_one"] : locale.Format("count.result_many", ("count", Invariant(total)));

        string pageTitle = (q, tag) switch
        {
            (not null, _) => locale.Format("title.search", ("q", q)),
            (null, not null) => locale.Format("title.tagged", ("tag", tag)),
            _ => locale["title.bookmarks"],
        };

        string listHref = Hrefs.List(tag, q);

        return BookmarksIndex.Result(
            locale: locale,
            Title: pageTitle,
            Assets: chrome.Assets,
            Nonce: chrome.Nonce,
            AntiforgeryFieldName: chrome.AntiforgeryFieldName,
            AntiforgeryToken: chrome.AntiforgeryToken,
            SignedIn: chrome.SignedIn,
            UserName: chrome.UserName,
            ActiveNav: "bookmarks",
            Heading: heading,
            CountText: countText,
            HasQuery: q is not null,
            HasTag: tag is not null,
            Query: q ?? "",
            Tag: tag ?? "",
            ClearSearchHref: Hrefs.List(tag, null),
            ClearFilterHref: Hrefs.List(null, q),
            Rows: viewRows,
            PrevHref: current > 1 ? Hrefs.Page(listHref, current - 1) : "",
            NextHref: current < totalPages ? Hrefs.Page(listHref, current + 1) : "",
            PositionText: locale.Format("pager.position", ("page", Invariant(current)), ("totalPages", Invariant(totalPages)), ("total", Invariant(total))));
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);""")

sub("""            var values = new BookmarkFormPage.Values(null, url, title, description, notes, tagsInput, isPublic);
            return FormResult(context, assets, antiforgery, values, formError);""",
    """            var values = new BookmarkFormValues(null, url, title, description, notes, tagsInput, isPublic);
            return FormResult(context, assets, antiforgery, values, formError);""")

sub("""        string? formError = canonical is null
            ? "Enter a valid http:// or https:// URL."
            : TagError(tags);""",
    """        LocaleCatalog errorLocale = Locales.Get(context.GetLocale());
        string? formError = canonical is null
            ? errorLocale["error.url_invalid"]
            : TagError(errorLocale, tags);""")

sub("""        var values = new BookmarkFormPage.Values(
            bookmark.Id, bookmark.Url, bookmark.Title, bookmark.Description, bookmark.Notes,
            TagRules.Format(tagRows.Select(t => t.Name)), bookmark.IsPublic);""",
    """        var values = new BookmarkFormValues(
            bookmark.Id, bookmark.Url, bookmark.Title, bookmark.Description, bookmark.Notes,
            TagRules.Format(tagRows.Select(t => t.Name)), bookmark.IsPublic);""")

sub("""        if (TagError(tags) is { } tagError)""",
    """        if (TagError(Locales.Get(context.GetLocale()), tags) is { } tagError)""")

sub("""            var values = new BookmarkFormPage.Values(
                bookmark.Id, bookmark.Url,""",
    """            var values = new BookmarkFormValues(
                bookmark.Id, bookmark.Url,""")

sub("""        return Results.Content(
            BookmarkDeletePage.Render(context, assets, antiforgery, bookmark.Id, bookmark.Url, bookmark.Title), Html);""",
    """        Chrome chrome = Chrome.For(context, assets, antiforgery);
        return BookmarkDelete.Result(
            locale: chrome.Locale,
            Title: chrome.Locale["title.bmdelete"],
            Assets: chrome.Assets,
            Nonce: chrome.Nonce,
            AntiforgeryFieldName: chrome.AntiforgeryFieldName,
            AntiforgeryToken: chrome.AntiforgeryToken,
            SignedIn: chrome.SignedIn,
            UserName: chrome.UserName,
            ActiveNav: "bookmarks",
            Id: bookmark.Id,
            Shown: bookmark.Title.Length == 0 ? bookmark.Url : bookmark.Title,
            Url: bookmark.Url);""")

sub("""    private static readonly BookmarkFormPage.Values EmptyForm = new(null, "", "", "", "", "", false);

    private static string? TagError(TagRules.ParsedTags tags) =>
        tags.Invalid.Count > 0
            ? $"{TagRules.NameShapeMessage} Rejected: {string.Join(", ", tags.Invalid)}."
            : tags.TooMany ? TagRules.TooManyMessage : null;""",
    """    private static readonly BookmarkFormValues EmptyForm = new(null, "", "", "", "", "", false);

    private static string? TagError(LocaleCatalog locale, TagRules.ParsedTags tags) =>
        tags.Invalid.Count > 0
            ? $"{locale["error.tag_shape"]} {locale.Format("error.tag_rejected", ("names", string.Join(", ", tags.Invalid)))}"
            : tags.TooMany
                ? locale.Format("error.tag_too_many", ("max", TagRules.MaxTagsPerBookmark.ToString(CultureInfo.InvariantCulture)))
                : null;""")

sub("""    private static IResult FormResult(HttpContext context, AssetUrls assets, IAntiforgery antiforgery, BookmarkFormPage.Values values, string? error) =>
        Results.Content(BookmarkFormPage.Render(context, assets, antiforgery, values, error), Html);""",
    """    private static IResult FormResult(HttpContext context, AssetUrls assets, IAntiforgery antiforgery, BookmarkFormValues values, string? error)
    {
        Chrome chrome = Chrome.For(context, assets, antiforgery);
        bool isEdit = values.Id is not null;
        return BookmarkForm.Result(
            locale: chrome.Locale,
            Title: chrome.Locale[isEdit ? "title.bmform_edit" : "title.bmform_new"],
            Assets: chrome.Assets,
            Nonce: chrome.Nonce,
            AntiforgeryFieldName: chrome.AntiforgeryFieldName,
            AntiforgeryToken: chrome.AntiforgeryToken,
            SignedIn: chrome.SignedIn,
            UserName: chrome.UserName,
            ActiveNav: "bookmarks",
            IsEdit: isEdit,
            Action: isEdit ? $"/bookmarks/{values.Id}/edit" : "/bookmarks/new",
            Url: values.Url,
            BookmarkTitle: values.Title,
            Description: values.Description,
            Notes: values.Notes,
            Tags: values.Tags,
            IsPublic: values.IsPublic,
            Error: error ?? "");
    }""")

if s == orig:
    print('NO CHANGES MADE')
    sys.exit(1)
open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('done')
