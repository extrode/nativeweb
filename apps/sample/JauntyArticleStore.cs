using System.Globalization;

using Extrode.JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

// The generated row type for a whole-table projection is named after the table,
// so Extrode.JauntyQ.Generated.Article and NativeWeb.Sample.Article are two different
// types with one name. C# would resolve the bare name to this file's own
// namespace and compile either way, which is exactly the kind of quiet
// resolution that makes a later reader mistrust every mapping below. Aliased so
// each side of the mapping says which type it is.
using Row = Extrode.JauntyQ.Generated.Article;

namespace NativeWeb.Sample;

/// <summary>
/// The same six methods as <see cref="MemoryStore{T}"/>, satisfied with JauntyQ
/// queries against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// This class is the point of the seam. Nothing above it changes: no generated
/// endpoint, no emitted form, no route, no view. <c>Program.cs</c> registers this
/// instead of <see cref="ArticleStore"/> when a connection string is configured,
/// and the admin UI cannot tell the difference. The scaffolding generator never
/// learns that a database exists.
/// </para>
/// <para>
/// Every method opens its own connection from the shared
/// <see cref="NpgsqlDataSource"/> and disposes it before returning, which is why
/// this can be a singleton: it holds no per-request state, and the pool -- not
/// the DI lifetime -- is what bounds connections.
/// </para>
/// </remarks>
internal sealed class JauntyArticleStore(NpgsqlDataSource dataSource) : IScaffoldStore<Article>
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <paramref name="searchFields"/> and <paramref name="sortFields"/> both arrive
    /// carrying the model's declared sets, and this implementation reads neither,
    /// because the columns are named in the SQL and JauntyQ compiles that text
    /// rather than composing it. The parameters are still the right shape for the
    /// seam: a store that builds its predicate at runtime needs the lists, and one
    /// whose queries are fixed statements has the same facts in its <c>.sql</c>
    /// files. Adding a fourth <c>[ScaffoldSearch]</c> means editing the query files
    /// to match, and a fourth <c>[ScaffoldSort]</c> means adding a pair of them;
    /// nothing here can do that for you.
    /// </para>
    /// <para>
    /// <b>Two static queries per sortable column, chosen by a switch.</b> The
    /// ordering cannot be spliced into the SQL -- JauntyQ generates from files at
    /// build time and there is no query builder -- so the validated property name
    /// selects which file runs. Nothing is ever interpolated, which is why an
    /// injection through <c>?sort=</c> is not merely blocked but unexpressible.
    /// </para>
    /// <para>
    /// The mirrored query serves two callers. A descending list walking forwards
    /// wants exactly what it returns; an ascending list walking BACKWARDS wants the
    /// same rows in the opposite order, so those come back reversed and are flipped
    /// here. Both are "the rows on the other side of the boundary", which is why
    /// eight files cover four columns in two directions each.
    /// </para>
    /// </remarks>
    public async ValueTask<IReadOnlyList<Article>> ListAsync(
        string? search,
        IReadOnlyList<string> searchFields,
        ScaffoldSortSpec? sort,
        IReadOnlyList<string> sortFields,
        ScaffoldCursor? after,
        int take,
        CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        string pattern = PatternFor(search);
        string property = sort?.Property ?? nameof(Article.Id);
        bool descending = sort?.Direction == ScaffoldSortDirection.Descending;
        bool backward = after?.Backward == true;

        // The empty key is how every query asks for its first page, and it is the
        // fallback for a cursor whose value will not parse as the column's type --
        // R9, and the reason a hand-edited ?after= renders the list rather than a
        // 500. It cannot collide with a real position: IScaffoldStore.CreateAsync
        // forbids a store returning an empty key.
        string afterId = after is null ? "" : after.Value.Key;
        string? afterValue = after?.SortValue;

        // Parsed here, above the mirror, because an unusable cursor has to reset the
        // direction as well as the position. created_at is a timestamp and a cursor is
        // text; round-trip form ("o") is what ScaffoldValues.CursorText wrote, so
        // anything else is a cursor this store did not issue. A null value is equally
        // unusable -- created_at is NOT NULL, so no cursor of ours omits it, and
        // DateTime.MinValue against a real key is a "position" that matches every row
        // ascending and no row descending. Falling back means the FIRST page in the
        // reader's chosen order: leaving backward set would run the opposite query
        // from the top and reverse it, landing them on the last page instead.
        DateTime afterCreated = default;
        if (property == nameof(Article.CreatedAt)
            && afterId.Length > 0
            && (afterValue is null
                || !DateTime.TryParse(
                    afterValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out afterCreated)))
        {
            afterId = "";

            // One row short, and only when the cursor being dropped was walking
            // backwards. The caller asks for PageSize + 1 to learn whether another page
            // exists and trims a BACKWARD page from its tail -- and its own `backward`
            // comes from the cursor, not from what this store decided to do with it. A
            // full take here comes back as an overflowing backward page and the first
            // row is trimmed away unseen. Measured 2026-09-01: page one rendered
            // starting at the second row.
            if (backward)
            {
                take = Math.Max(take - 1, 0);
                backward = false;
            }
        }

        // Walking backwards runs the opposite query, then flips the rows into
        // display order. The two knobs are independent: an ascending list walking
        // backwards and a descending list walking forwards both run the descending
        // query, and only the first of them reverses what comes back.
        bool mirrored = descending != backward;

        List<Row> rows = property switch
        {
            nameof(Article.Title) => mirrored
                ? await db.Articles.ListByTitleDescAsync(pattern, afterValue ?? "", afterId, take, ct).ConfigureAwait(false)
                : await db.Articles.ListByTitleAscAsync(pattern, afterValue ?? "", afterId, take, ct).ConfigureAwait(false),

            // Author is the nullable column, so its cursor value passes through as
            // null rather than being coerced to "". The two queries read that null
            // to tell "the reader is inside the NULL block" from "the reader is
            // before it", which is a distinction an empty string cannot carry --
            // and getting it wrong repeats every author-less row.
            nameof(Article.Author) => mirrored
                ? await db.Articles.ListByAuthorDescAsync(pattern, afterValue, afterId, take, ct).ConfigureAwait(false)
                : await db.Articles.ListByAuthorAscAsync(pattern, afterValue, afterId, take, ct).ConfigureAwait(false),

            nameof(Article.CreatedAt) => mirrored
                ? await db.Articles.ListByCreatedDescAsync(pattern, afterCreated, afterId, take, ct).ConfigureAwait(false)
                : await db.Articles.ListByCreatedAscAsync(pattern, afterCreated, afterId, take, ct).ConfigureAwait(false),

            _ => mirrored
                ? await db.Articles.ListByKeyDescAsync(pattern, afterId, take, ct).ConfigureAwait(false)
                : await db.Articles.ListByKeyAscAsync(pattern, afterId, take, ct).ConfigureAwait(false),
        };

        var articles = new List<Article>(rows.Count);
        foreach (Row row in rows)
        {
            articles.Add(Map(row));
        }

        if (backward)
        {
            articles.Reverse();
        }

        return articles;
    }

    /// <inheritdoc/>
    /// <remarks>Same fixed-SQL reasoning as <see cref="ListAsync"/>.</remarks>
    public async ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        Articles.Result.Count? total = await db.Articles.CountAsync(PatternFor(search), ct).ConfigureAwait(false);

        // COUNT(*) is bigint. The seam returns int because it feeds a pager, and a
        // table with more than two billion rows has a bigger problem than paging.
        return total is null ? 0 : (int)Math.Min(total.Total, int.MaxValue);
    }

    public async ValueTask<Article?> GetAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        Row? row = await db.Articles.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        // One more query for the row's side of the relation (spec 023 R1): the
        // edit form ticks what this list holds. The list pages never carry it.
        Article article = Map(row);
        List<ArticleTags.Result.ListSlugsForArticle> links =
            await db.ArticleTags.ListSlugsForArticleAsync(id, ct).ConfigureAwait(false);
        article.Tags = links.Select(l => l.TagSlug).ToList();

        return article;
    }

    public async ValueTask<string> CreateAsync(Article item, CancellationToken ct)
    {
        // Same key shape as the in-memory store, so switching stores does not change
        // what a URL looks like. Eight hex characters is a demo's choice: the PK
        // constraint turns a collision into a failed insert rather than a lost row,
        // which is the right failure, but a real consumer wants a full uuid.
        string key = Guid.NewGuid().ToString("n")[..8];

        // Stamped here for the same reason ArticleStore.OnCreate stamps it: the
        // generator marks CreatedAt read-only, so no form can carry it and the store
        // is the only thing left that can set it.
        item.Id = key;
        item.CreatedAt = DateTime.UtcNow;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        // Insert first and let articles_slug_key say whether the slug is free: a
        // pre-check would let two concurrent creates of one title both pass it. A
        // typed slug that collides is refused with the message the memory store
        // gives; a derived one is retried with the next suffix.
        //
        // The row and its links are one transaction (spec 023 R1, R7). A tag or
        // category key that names no row fails its foreign key, and the attempt
        // rolls back whole, so a refused form has saved nothing. A failed statement
        // poisons a PostgreSQL transaction, which is why the slug retry begins a
        // new one rather than issuing a second INSERT inside the first.
        string? typed = TypedSlug(item);
        string slug = typed ?? Slugs.Derive(item.Title);
        for (int attempt = 2; ; attempt++)
        {
            using JauntyDb.Transaction tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await db.Articles.InsertAsync(
                    key,
                    slug,
                    item.Title,
                    item.Author,
                    item.Body,
                    StatusText(item.Status),
                    DayStart(item.PublishedAt),
                    KeyOrNull(item.CategoryKey),
                    item.ViewCount,
                    item.CreatedAt,
                    ct).ConfigureAwait(false);
                await LinkAsync(db, key, item.Tags, ct).ConfigureAwait(false);
                tx.Commit();
                item.Slug = slug;
                return key;
            }
            catch (PostgresException taken) when (IsSlugTaken(taken))
            {
                if (typed is not null)
                {
                    throw new ScaffoldRefusedException(Slugs.TakenMessage(typed));
                }

                slug = string.Create(CultureInfo.InvariantCulture, $"{Slugs.Derive(item.Title)}-{attempt}");
            }
            catch (PostgresException missing) when (IsMissingChoice(missing))
            {
                throw new ScaffoldRefusedException(MissingChoice(missing));
            }
        }
    }

    public async ValueTask<bool> UpdateAsync(string id, Article item, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        // Rows affected, not a prior existence check: a SELECT-then-UPDATE pair
        // answers the wrong question under concurrency, and UPDATE already reports
        // whether it matched.
        // A null slug reaches COALESCE(@slug, slug) in Update.sql and keeps the
        // row's own; a typed one is validated here and made unique by the index.
        // The links are rewritten whole -- delete, then one insert per posted key --
        // in the same transaction as the row, so the article's side of the
        // relation is exactly the ticked set or nothing changed (spec 023 R1).
        string? typed = TypedSlug(item);
        using JauntyDb.Transaction tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int affected = await db.Articles.UpdateAsync(
                item.Title,
                item.Author,
                item.Body,
                StatusText(item.Status),
                DayStart(item.PublishedAt),
                KeyOrNull(item.CategoryKey),
                typed,
                id,
                ct).ConfigureAwait(false);
            if (affected == 0)
            {
                return false;
            }

            await db.ArticleTags.DeleteForArticleAsync(id, ct).ConfigureAwait(false);
            await LinkAsync(db, id, item.Tags, ct).ConfigureAwait(false);
            tx.Commit();
            return true;
        }
        catch (PostgresException taken) when (IsSlugTaken(taken))
        {
            throw new ScaffoldRefusedException(Slugs.TakenMessage(typed ?? ""));
        }
        catch (PostgresException missing) when (IsMissingChoice(missing))
        {
            throw new ScaffoldRefusedException(MissingChoice(missing));
        }
    }

    private static async Task LinkAsync(JauntyDb db, string articleId, IReadOnlyList<string> tagSlugs, CancellationToken ct)
    {
        foreach (string slug in tagSlugs)
        {
            await db.ArticleTags.InsertAsync(articleId, slug, ct).ConfigureAwait(false);
        }
    }

    private static Article Map(Row row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Slug = row.Slug,
        Author = row.Author,
        Body = row.Body,
        Status = StatusOf(row.Status),
        PublishedAt = DayOf(row.PublishedAt),
        CategoryKey = row.CategoryKey,
        ViewCount = row.ViewCount,
        CreatedAt = row.CreatedAt,
    };

    // The status column holds the enum's members in lower case, held to those
    // three by articles_status_check; anything else in the column reads as Draft,
    // which is the safe direction for a public site.
    internal static string StatusText(ArticleStatus status) => status switch
    {
        ArticleStatus.Published => "published",
        ArticleStatus.Archived => "archived",
        _ => "draft",
    };

    internal static ArticleStatus StatusOf(string text) => text switch
    {
        "published" => ArticleStatus.Published,
        "archived" => ArticleStatus.Archived,
        _ => ArticleStatus.Draft,
    };

    // DATE crosses the wire as a DateTime in both directions: JauntyQ maps the
    // column to System.DateTime, and the queries cast the parameter with ::date.
    // Unspecified kind on the way out, because Npgsql refuses a UTC-kind value
    // for a timestamp without time zone, which is what the parameter is typed as.
    internal static DateTime? DayStart(DateOnly? day) => day?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

    internal static DateOnly? DayOf(DateTime? at) => at is DateTime when ? DateOnly.FromDateTime(when) : null;

    // The form posts "" for the "(none)" option; the column wants NULL.
    internal static string? KeyOrNull(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    // A posted key that names no row fails its foreign key (spec 023 R7). Which
    // key is read off the constraint, so both stores give the memory stores'
    // message for the same field.
    internal static bool IsMissingChoice(PostgresException e) =>
        e.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    internal static string MissingChoice(PostgresException e) =>
        Refusals.MissingChoice(
            e.ConstraintName?.Contains("category", StringComparison.Ordinal) == true ? "Category"
            : e.ConstraintName?.Contains("tag_slug", StringComparison.Ordinal) == true ? "Tags"
            : "Articles");

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        int affected = await db.Articles.DeleteAsync(id, ct).ConfigureAwait(false);
        return affected > 0;
    }

    // The slug rules shared with ArticleStore (spec 022 R2): blank means derive
    // on create and keep on update; typed must match the alphabet. Uniqueness is
    // the unique index's to enforce, not a query's to pre-check.
    private static string? TypedSlug(Article item)
    {
        if (string.IsNullOrWhiteSpace(item.Slug))
        {
            return null;
        }

        string typed = item.Slug.Trim();
        return Slugs.IsValid(typed) ? typed : throw new ScaffoldRefusedException(Slugs.InvalidMessage);
    }

    private static bool IsSlugTaken(PostgresException e) =>
        e.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(e.ConstraintName, "articles_slug_key", StringComparison.Ordinal);

    /// <summary>
    /// Turns a search box's contents into the ILIKE pattern the queries take.
    /// </summary>
    /// <remarks>
    /// The escaping is the part that matters. Without it a search for <c>%</c>
    /// matches every row, <c>_</c> matches any single character, and a trailing
    /// backslash makes the pattern malformed -- all three are things a user can type
    /// into the scaffolded search box by accident. PostgreSQL's ILIKE takes
    /// backslash as its default escape character, so each of the three is escaped
    /// with one.
    /// </remarks>
    internal static string PatternFor(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? "%"
            : "%" + search
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal)
              + "%";
}
