# R42 conversion helper: rewrites TagEndpoints, TransferEndpoints and
# PublicEndpoints from the old string-builder pages to generated templates.
# Safe to delete after V4 merges.
import sys

changed = []


def rewrite(path, subs):
    s = open(path, encoding='utf-8').read()
    orig = s
    for old, new in subs:
        if old not in s:
            print('MISS in %s: %s' % (path, old[:70].replace('\n', '\\n')))
            continue
        s = s.replace(old, new)
    if s != orig:
        open(path, 'w', encoding='utf-8', newline='\n').write(s)
        changed.append(path)


CHROME_ARGS = """            Assets: chrome.Assets,
            Nonce: chrome.Nonce,
            AntiforgeryFieldName: chrome.AntiforgeryFieldName,
            AntiforgeryToken: chrome.AntiforgeryToken,
            SignedIn: chrome.SignedIn,
            UserName: chrome.UserName,"""

rewrite('apps/devmarks/Tags/TagEndpoints.cs', [
    ("""using NativeWeb.DevMarks.Auth;
using NativeWeb.DevMarks.Render;

using Npgsql;
""",
     """using NativeWeb.DevMarks.Auth;
using NativeWeb.DevMarks.Render;
using NativeWeb.Render;

using Npgsql;

using TagDelete = NativeWeb.DevMarks.Templates.Tags.Delete;
using TagsIndex = NativeWeb.DevMarks.Templates.Tags.Index;
"""),
    ("""        List<JauntyQ.Generated.Tags.Result.ListWithCounts> rows = await db.Tags.ListWithCountsAsync(CurrentUserId(context), ct).ConfigureAwait(false);
        var viewRows = rows.Select(r => new TagsPage.Row(r.Id, r.Name, r.IsPublic, r.BookmarkCount ?? 0)).ToList();

        return Results.Content(TagsPage.Render(context, assets, antiforgery, viewRows, error), Html);
    }""",
     """        List<JauntyQ.Generated.Tags.Result.ListWithCounts> rows = await db.Tags.ListWithCountsAsync(CurrentUserId(context), ct).ConfigureAwait(false);

        Chrome chrome = Chrome.For(context, assets, antiforgery);
        LocaleCatalog locale = chrome.Locale;
        var viewRows = rows.Select(r => new TagRow(
            r.Id, r.Name, Hrefs.Tag(r.Name), r.IsPublic, BookmarkCountText(locale, r.BookmarkCount ?? 0))).ToList();

        return TagsIndex.Result(
            locale: locale,
            Title: locale["title.tags"],
%s
            ActiveNav: "tags",
            Rows: viewRows,
            CountText: locale.Format("tags.count", ("count", Invariant(rows.Count))),
            Error: error ?? "");
    }

    private static string BookmarkCountText(LocaleCatalog locale, int count) =>
        count == 1 ? locale["count.bookmark_one"] : locale.Format("count.bookmark_many", ("count", Invariant(count)));

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);""" % CHROME_ARGS),
    ("""            return await GetList(context, assets, antiforgery, TagRules.NameShapeMessage, ct).ConfigureAwait(false);""",
     """            return await GetList(context, assets, antiforgery, Locales.Get(context.GetLocale())["error.tag_shape"], ct).ConfigureAwait(false);"""),
    ("""        return Results.Content(
            TagDeletePage.Render(context, assets, antiforgery, tag.Id, tag.Name, tag.BookmarkCount ?? 0), Html);""",
     """        Chrome chrome = Chrome.For(context, assets, antiforgery);
        return TagDelete.Result(
            locale: chrome.Locale,
            Title: chrome.Locale["title.tagdelete"],
%s
            ActiveNav: "tags",
            Id: tag.Id,
            Name: tag.Name,
            OnCountText: chrome.Locale.Format("tagdelete.on_count", ("count", BookmarkCountText(chrome.Locale, tag.BookmarkCount ?? 0))));""" % CHROME_ARGS),
])

rewrite('apps/devmarks/Transfer/TransferEndpoints.cs', [
    ("""using NativeWeb.DevMarks.Render;
using NativeWeb.DevMarks.Tags;

using Npgsql;
""",
     """using NativeWeb.DevMarks.Render;
using NativeWeb.DevMarks.Tags;
using NativeWeb.Render;

using Npgsql;

using TransferImport = NativeWeb.DevMarks.Templates.Transfer.Import;
using TransferReport = NativeWeb.DevMarks.Templates.Transfer.Report;
"""),
    ("""    private const string TooLargeError = "That file is over 5 MB. Export files are far smaller — this is probably not a bookmarks file.";
    private const string UnrecognizedError = "That doesn't look like a bookmarks HTML export or a DevMarks JSON export.";
    private const string NothingToImportError = "Choose a file or paste the text of one.";""",
     """    // Catalog keys, resolved to the request's locale by ImportResult.
    private const string TooLargeError = "error.import_too_large";
    private const string UnrecognizedError = "error.import_unrecognized";
    private const string NothingToImportError = "error.import_nothing";"""),
    ("""        var report = new ImportReportPage.Report(created, updated, skippedInvalidUrl, droppedTags);
        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NativeWeb.DevMarks.Transfer");
        LogImport(logger, userId, created, updated, skippedInvalidUrl, droppedTags);
        return Results.Content(ImportReportPage.Render(context, assets, antiforgery, report), Html);""",
     """        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NativeWeb.DevMarks.Transfer");
        LogImport(logger, userId, created, updated, skippedInvalidUrl, droppedTags);

        Chrome chrome = Chrome.For(context, assets, antiforgery);
        LocaleCatalog locale = chrome.Locale;
        int totalImported = created + updated;
        return TransferReport.Result(
            locale: locale,
            Title: locale["title.report"],
%s
            ActiveNav: "import",
            Summary: totalImported == 1
                ? locale["report.summary_one"]
                : locale.Format("report.summary_many", ("count", Invariant(totalImported))),
            Created: created,
            Updated: updated,
            SkippedInvalidUrl: skippedInvalidUrl,
            DroppedTags: droppedTags,
            DroppedDetail: locale.Format("report.dropped", ("limit", Invariant(TagRules.MaxTagsPerBookmark))));""" % CHROME_ARGS),
    ("""    private static IResult ImportResult(HttpContext context, AssetUrls assets, IAntiforgery antiforgery, string? error) =>
        Results.Content(ImportPage.Render(context, assets, antiforgery, error), Html);""",
     """    private static IResult ImportResult(HttpContext context, AssetUrls assets, IAntiforgery antiforgery, string? errorKey)
    {
        Chrome chrome = Chrome.For(context, assets, antiforgery);
        return TransferImport.Result(
            locale: chrome.Locale,
            Title: chrome.Locale["title.import"],
%s
            ActiveNav: "import",
            Error: errorKey is null ? "" : chrome.Locale[errorKey]);
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);""" % CHROME_ARGS),
])

rewrite('apps/devmarks/Public/PublicEndpoints.cs', [
    ("""using JauntyQ.Generated;
""",
     """using System.Globalization;

using JauntyQ.Generated;
"""),
    ("""using NativeWeb.DevMarks.Render;

using Npgsql;
""",
     """using NativeWeb.DevMarks.Render;
using NativeWeb.Render;

using Npgsql;

using PublicProfile = NativeWeb.DevMarks.Templates.Public.Profile;
"""),
    ("""        IReadOnlyList<PublicPage.Row> viewRows;
        if (tag is null)
        {
            viewRows = (await db.Public.ListPageAsync(PageSize, offset, user.Id, ct).ConfigureAwait(false))
                .Select(r => new PublicPage.Row(
                    r.Id, r.Url, r.Title, r.Description, r.FaviconContentType is not null, r.CreatedAt, SplitTags(r.Tags)))
                .ToList();
        }
        else
        {
            viewRows = (await db.Public.ListPageByTagAsync(PageSize, offset, user.Id, tag, ct).ConfigureAwait(false))
                .Select(r => new PublicPage.Row(
                    r.Id, r.Url, r.Title, r.Description, r.FaviconContentType is not null, r.CreatedAt, SplitTags(r.Tags)))
                .ToList();
        }

        // The collection chips only render on the profile page; the collection
        // page skips the query rather than fetching a list it will not draw.
        IReadOnlyList<PublicPage.PublicTag> collections = tag is null
            ? (await db.Public.TagsAsync(user.Id, ct).ConfigureAwait(false))
                .Select(row => new PublicPage.PublicTag(row.Name, row.BookmarkCount ?? 0))
                .ToList()
            : [];

        return Results.Content(
            PublicPage.Render(context, assets, antiforgery, user.Username, tag, viewRows, collections, current, totalPages, total),
            Html);
    }""",
     """        // Rows here are all public by construction; Owned=false in the template
        // suppresses the badge and the edit/delete actions, and the tag chips
        // point into this user's public collections rather than the signed-in
        // filter.
        Func<string, string> tagHref = t => Hrefs.PublicTag(user.Username, t);
        IReadOnlyList<BookmarkRow> viewRows;
        if (tag is null)
        {
            viewRows = (await db.Public.ListPageAsync(PageSize, offset, user.Id, ct).ConfigureAwait(false))
                .Select(r => BookmarkRow.Create(
                    r.Id, r.Url, r.Title, r.Description, r.FaviconContentType is not null, true, r.CreatedAt, SplitTags(r.Tags), tagHref))
                .ToList();
        }
        else
        {
            viewRows = (await db.Public.ListPageByTagAsync(PageSize, offset, user.Id, tag, ct).ConfigureAwait(false))
                .Select(r => BookmarkRow.Create(
                    r.Id, r.Url, r.Title, r.Description, r.FaviconContentType is not null, true, r.CreatedAt, SplitTags(r.Tags), tagHref))
                .ToList();
        }

        // The collection chips only render on the profile page; the collection
        // page skips the query rather than fetching a list it will not draw.
        IReadOnlyList<PublicTagChip> collections = tag is null
            ? (await db.Public.TagsAsync(user.Id, ct).ConfigureAwait(false))
                .Select(row => new PublicTagChip(
                    $"{row.Name} ({(row.BookmarkCount ?? 0).ToString(CultureInfo.InvariantCulture)})",
                    Hrefs.PublicTag(user.Username, row.Name)))
                .ToList()
            : [];

        Chrome chrome = Chrome.For(context, assets, antiforgery);
        LocaleCatalog locale = chrome.Locale;
        string baseHref = tag is null ? Hrefs.Profile(user.Username) : Hrefs.PublicTag(user.Username, tag);

        return PublicProfile.Result(
            locale: locale,
            Title: tag is null
                ? locale.Format("title.public", ("username", user.Username))
                : locale.Format("title.public_tag", ("username", user.Username), ("tag", tag)),
%s
            ActiveNav: "",
            Heading: tag is null
                ? locale.Format("public.heading_all", ("username", user.Username))
                : locale.Format("public.heading_tag", ("username", user.Username), ("tag", tag)),
            CountText: total == 1
                ? locale["count.bookmark_one"]
                : locale.Format("count.bookmark_many", ("count", total.ToString(CultureInfo.InvariantCulture))),
            HasTag: tag is not null,
            BackHref: Hrefs.Profile(user.Username),
            Collections: collections,
            Rows: viewRows,
            PrevHref: current > 1 ? Hrefs.Page(baseHref, current - 1) : "",
            NextHref: current < totalPages ? Hrefs.Page(baseHref, current + 1) : "",
            PositionText: locale.Format(
                "pager.position",
                ("page", current.ToString(CultureInfo.InvariantCulture)),
                ("totalPages", totalPages.ToString(CultureInfo.InvariantCulture)),
                ("total", total.ToString(CultureInfo.InvariantCulture))));
    }""" % CHROME_ARGS),
])

print('changed:', changed)
if len(changed) != 3:
    sys.exit(1)
