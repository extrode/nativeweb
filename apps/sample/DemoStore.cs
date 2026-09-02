using System.Collections.Concurrent;

using NativeWeb.Scaffold;

namespace NativeWeb.Sample;

/// <summary>
/// An in-memory <see cref="IScaffoldStore{T}"/> so the demo runs with no database.
/// </summary>
/// <remarks>
/// NOT the shape a real consumer ships: the project's binding rule is that all data
/// access goes through JauntyQ, and a production store implements these six methods
/// with JauntyQ queries. What this class demonstrates is that the generated
/// endpoints depend only on the contract -- swapping this for a JauntyQ-backed store
/// changes nothing above it, and no generated code has to be regenerated.
/// </remarks>
internal abstract class MemoryStore<T> : IScaffoldStore<T>
    where T : class
{
    private readonly ConcurrentDictionary<string, T> _items = new(StringComparer.Ordinal);

    protected abstract string KeyOf(T item);

    protected abstract void AssignKey(T item, string key);

    /// <summary>
    /// One searchable property's value, by the declared property name the generator
    /// passes down, or null for a name this model does not carry.
    /// </summary>
    /// <remarks>
    /// This replaced a hand-written <c>Matches</c> that named Title, Body and Author
    /// itself. The list it named and the list <c>[ScaffoldSearch]</c> declared were
    /// two copies of one fact, and nothing compared them: removing the attribute
    /// changed the search box's placeholder and left the column searchable, which is
    /// the defect this parameter exists to make impossible.
    /// </remarks>
    protected abstract string? Field(T item, string name);

    /// <summary>
    /// One sortable property's value as something comparable, by the declared
    /// property name, falling back to the key for a name this model does not sort by.
    /// </summary>
    /// <remarks>
    /// A <c>switch</c> and not reflection, for the same reason <see cref="Field"/> is
    /// one: this runs inside a NativeAOT binary, where <c>GetProperty(name)</c>
    /// compiles and then returns null at run time. Returning the key by default is
    /// what makes an unrecognised property name the default order rather than a
    /// throw -- the generated endpoint has already rejected anything outside the
    /// allow-list, so a name arriving here came from a hand-written caller.
    /// </remarks>
    protected abstract IComparable? SortValue(T item, string name);

    protected virtual void OnCreate(T item)
    {
    }

    /// <summary>
    /// The last word before a row is written, for create and update alike. Runs
    /// under the save lock, so a rule that reads the other rows (a value that must
    /// be unique) sees a table no concurrent save is halfway through. Throw
    /// <see cref="ScaffoldRefusedException"/> to refuse; the scaffolded form shows
    /// the message.
    /// </summary>
    /// <param name="item">What is about to be stored, its key already assigned.</param>
    /// <param name="existing">The row being replaced, or null on create.</param>
    protected virtual void BeforeSave(T item, T? existing)
    {
    }

    /// <summary>
    /// After a row is removed, under the save lock, so a cascade into another
    /// store or the relation cannot interleave with a save that reads either.
    /// </summary>
    protected virtual void OnDelete(string key)
    {
    }

    /// <summary>
    /// Fills what the row holds outside its own dictionary entry, on the way out
    /// of <see cref="GetAsync"/>: a relation's keys live in <see cref="MemoryRelation"/>,
    /// and the form that edits them reads them from here.
    /// </summary>
    protected virtual void Load(T item)
    {
    }

    /// <summary>Every row, for a rule in <see cref="BeforeSave"/> that must look across them.</summary>
    protected IEnumerable<T> Items => this._items.Values;

    private readonly Lock _saves = new();

    /// <summary>The key a new row gets; a store whose caller may supply one overrides this.</summary>
    protected virtual string KeyFor(T item) => Guid.NewGuid().ToString("n")[..8];

    /// <summary>Whether a row with this key is held, for another store's choice check.</summary>
    internal bool Has(string key) => this._items.ContainsKey(key);

    public ValueTask<IReadOnlyList<T>> ListAsync(
        string? search,
        IReadOnlyList<string> searchFields,
        ScaffoldSortSpec? sort,
        IReadOnlyList<string> sortFields,
        ScaffoldCursor? after,
        int take,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        string property = sort?.Property ?? string.Empty;
        bool descending = sort?.Direction == ScaffoldSortDirection.Descending;

        List<T> ordered = [.. Filter(search, searchFields)];
        ordered.Sort((a, b) => Compare(a, b, property, descending));

        // Located by key, not by comparing the cursor's sort value. This store holds
        // every row, so the boundary row is either present or gone, and finding it is
        // exact even when a hundred rows share the sorted value. A SQL store cannot
        // do this -- it has to express the boundary as a predicate, which is why
        // JauntyArticleStore parses the value and compares -- and the two are
        // equivalent only because the key breaks every tie in both.
        //
        // A boundary row that has since been deleted lands the reader on the first
        // page. That is a real behaviour difference from the SQL store, which keeps
        // its place because a value comparison does not need the row to exist.
        int at = after is ScaffoldCursor cursor
            ? ordered.FindIndex(item => string.Equals(KeyOf(item), cursor.Key, StringComparison.Ordinal))
            : -1;

        if (at < 0)
        {
            // One row short of `take` when the lost cursor was walking backwards. The
            // caller asks for PageSize + 1 to learn whether another page exists and
            // trims a backward page from its TAIL, so a full take here reads as an
            // overflowing backward page and the first row is trimmed away unseen.
            int first = after?.Backward == true ? Math.Max(take - 1, 0) : take;
            return ValueTask.FromResult<IReadOnlyList<T>>(ordered.GetRange(0, Math.Min(first, ordered.Count)));
        }

        if (after!.Value.Backward)
        {
            // The rows immediately before the boundary, still in display order. Taking
            // the FIRST `take` of everything before it would be a different page
            // entirely, and one the reader can reach from neither direction.
            int start = Math.Max(0, at - take);
            return ValueTask.FromResult<IReadOnlyList<T>>(ordered.GetRange(start, at - start));
        }

        int from = at + 1;
        return ValueTask.FromResult<IReadOnlyList<T>>(ordered.GetRange(from, Math.Min(take, ordered.Count - from)));
    }

    // Nulls last ascending, first descending -- PostgreSQL's own default, so the
    // reference store and the SQL store agree about where an author-less row sits.
    // The key breaks every tie, including between two nulls, or a page boundary
    // inside a block of equal values would have no single answer and the rows there
    // would repeat.
    private int Compare(T a, T b, string property, bool descending)
    {
        IComparable? x = SortValue(a, property);
        IComparable? y = SortValue(b, property);

        // Ordinal for text, because IComparable.CompareTo on a boxed string is
        // CurrentCulture: the same rows would come back in a different order on a
        // machine with a different locale, and the key tiebreaker below is ordinal
        // either way. Ordering is a fact about the data, not about the host.
        int result = (x, y) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            (string a1, string b1) => string.CompareOrdinal(a1, b1),
            _ => x.CompareTo(y),
        };

        if (result == 0)
        {
            result = string.CompareOrdinal(KeyOf(a), KeyOf(b));
        }

        return descending ? -result : result;
    }

    public ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct) =>
        ValueTask.FromResult(Filter(search, searchFields).Count());

    public ValueTask<T?> GetAsync(string id, CancellationToken ct)
    {
        if (!this._items.TryGetValue(id, out T? found))
        {
            return ValueTask.FromResult<T?>(null);
        }

        Load(found);
        return ValueTask.FromResult<T?>(found);
    }

    public ValueTask<string> CreateAsync(T item, CancellationToken ct)
    {
        string key = KeyFor(item);
        AssignKey(item, key);
        OnCreate(item);
        lock (this._saves)
        {
            BeforeSave(item, null);
            this._items[key] = item;
        }

        return ValueTask.FromResult(key);
    }

    public ValueTask<bool> UpdateAsync(string id, T item, CancellationToken ct)
    {
        // TryUpdate against the row we read, not ContainsKey then an indexer
        // assignment. The generated handler now treats false as 404, and it does so
        // because a concurrent delete between the load and the save is one request,
        // not a rare race -- a check-then-act here would resurrect the deleted row
        // and report success, which is the exact thing the handler was changed to
        // stop reporting. JauntyArticleStore gets this right through rows-affected;
        // the reference store has to match the semantics it demonstrates.
        //
        // The loop is what keeps false meaning "no such row" and nothing else. A
        // single TryUpdate also fails when a concurrent UPDATE replaced the value,
        // because it compares by reference; IScaffoldStore.UpdateAsync defines false
        // strictly as absent and the handler maps it to 404, so two admins saving
        // the same row left the loser told the record does not exist while it was
        // still in the list. Retrying until the row is either gone or ours makes the
        // concurrent-update case last-write-wins, which is what a SQL UPDATE's
        // rows-affected reports.
        AssignKey(item, id);

        // The lock is for BeforeSave, whose rules read the other rows; the loop
        // above still carries the semantics, and the lock makes its retry moot
        // rather than wrong.
        lock (this._saves)
        {
            while (this._items.TryGetValue(id, out T? current))
            {
                BeforeSave(item, current);
                if (this._items.TryUpdate(id, item, current))
                {
                    return ValueTask.FromResult(true);
                }
            }
        }

        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        lock (this._saves)
        {
            if (!this._items.TryRemove(id, out _))
            {
                return ValueTask.FromResult(false);
            }

            OnDelete(id);
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>Seeds a row directly, bypassing key generation.</summary>
    internal void Seed(T item) => this._items[KeyOf(item)] = item;

    private IEnumerable<T> Filter(string? search, IReadOnlyList<string> searchFields) =>
        string.IsNullOrWhiteSpace(search) || searchFields.Count == 0
            ? this._items.Values
            : this._items.Values.Where(i => Matches(i, search!, searchFields));

    private bool Matches(T item, string search, IReadOnlyList<string> searchFields)
    {
        foreach (string name in searchFields)
        {
            if (Field(item, name) is string value
                && value.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The three memory stores and the relation between two of them, wired once. A
/// store checks a posted key against its peer through a delegate rather than a
/// reference, because articles point at tags and tags point back at articles,
/// and two constructors cannot each take the other's result.
/// </summary>
internal sealed record MemoryStores(
    ArticleStore Articles,
    TagStore Tags,
    CategoryStore Categories,
    MemoryRelation Relation)
{
    internal static MemoryStores Create()
    {
        var relation = new MemoryRelation();
        ArticleStore articles = null!;
        var tags = new TagStore(relation, id => articles.Has(id));
        var categories = new CategoryStore(key => articles.ClearCategory(key));
        articles = new ArticleStore(relation, tags.Has, categories.Has);

        return new MemoryStores(articles, tags, categories, relation);
    }
}

internal sealed class ArticleStore(
    MemoryRelation relation,
    Func<string, bool>? tagExists,
    Func<string, bool>? categoryExists) : MemoryStore<Article>
{
    /// <summary>A store on its own: no tag or category exists, so every posted key is refused.</summary>
    public ArticleStore()
        : this(new MemoryRelation(), null, null)
    {
    }

    protected override string KeyOf(Article item) => item.Id;

    protected override void AssignKey(Article item, string key) => item.Id = key;

    // The generator marks CreatedAt read-only, so the form never carries it and
    // the store is the only thing that can set it. That is the intended division.
    protected override void OnCreate(Article item) => item.CreatedAt = DateTime.UtcNow;

    // The slug rules (spec 022 R2), the same ones JauntyArticleStore applies:
    // blank derives from the title on create and keeps the row's own on edit; a
    // typed one must match the alphabet and be free. Runs under the save lock, so
    // two concurrent creates of one title cannot both see the derived slug as free.
    protected override void BeforeSave(Article item, Article? existing)
    {
        Choices(item);

        string? typed = string.IsNullOrWhiteSpace(item.Slug) ? null : item.Slug.Trim();
        if (typed is null)
        {
            item.Slug = existing?.Slug ?? Slugs.Unique(Slugs.Derive(item.Title), Taken(item.Id));
            Link(item);
            return;
        }

        if (!Slugs.IsValid(typed))
        {
            throw new ScaffoldRefusedException(Slugs.InvalidMessage);
        }

        if (Taken(item.Id)(typed))
        {
            throw new ScaffoldRefusedException(Slugs.TakenMessage(typed));
        }

        item.Slug = typed;
        Link(item);
    }

    // The store is the authority on whether a posted key names a row (R7): the
    // form only offers what existed when it was rendered. Every check runs
    // before anything is written, so a refusal leaves the relation and the row
    // as they were. A blank category is "none", which the form posts as the
    // empty option.
    private void Choices(Article item)
    {
        if (string.IsNullOrWhiteSpace(item.CategoryKey))
        {
            item.CategoryKey = null;
        }
        else if (categoryExists?.Invoke(item.CategoryKey) != true)
        {
            throw new ScaffoldRefusedException(Refusals.MissingChoice("Category"));
        }

        if (item.Tags.Any(slug => tagExists?.Invoke(slug) != true))
        {
            throw new ScaffoldRefusedException(Refusals.MissingChoice("Tags"));
        }
    }

    // After every rule has passed: the row is about to be written, so its side
    // of the relation is too.
    private void Link(Article item)
    {
        relation.ReplaceTagsOf(item.Id, item.Tags);
        item.Tags = relation.TagsOf(item.Id);
    }

    protected override void Load(Article item) => item.Tags = relation.TagsOf(item.Id);

    protected override void OnDelete(string key) => relation.RemoveArticle(key);

    /// <summary>What a category's delete does to the rows that pointed at it: nothing points any more.</summary>
    internal void ClearCategory(string key)
    {
        foreach (Article article in Items.Where(a => string.Equals(a.CategoryKey, key, StringComparison.Ordinal)))
        {
            article.CategoryKey = null;
        }
    }

    private Func<string, bool> Taken(string ownId) =>
        slug => Items.Any(a => a.Id != ownId && string.Equals(a.Slug, slug, StringComparison.Ordinal));

    protected override string? Field(Article item, string name) => name switch
    {
        nameof(Article.Title) => item.Title,
        nameof(Article.Author) => item.Author,
        nameof(Article.Body) => item.Body,
        _ => null,
    };

    protected override IComparable? SortValue(Article item, string name) => name switch
    {
        nameof(Article.Title) => item.Title,
        nameof(Article.Author) => item.Author,
        nameof(Article.CreatedAt) => item.CreatedAt,
        _ => item.Id,
    };
}

/// <summary>
/// The article-tag relation on the no-database path (spec 022 R4): article id to
/// the slugs of its tags and the reverse, held beside <see cref="ArticleStore"/>
/// and <see cref="TagStore"/> rather than inside either, since both public read
/// seams need it and neither store owns the other's rows.
/// </summary>
/// <remarks>
/// Both sides are held, as two indexes over one set of links, so either form can
/// replace its own side (spec 023 R1, R2) and the other side sees it. Two admins
/// saving the article and one of its tags at once is last-write-wins across the
/// two, the same as two UPDATEs of one join table. The stores cascade: deleting
/// an article or a tag removes its links here, which is the in-memory answer to
/// the <c>ON DELETE CASCADE</c> the real join table carries (R9).
/// </remarks>
internal sealed class MemoryRelation
{
    private readonly Dictionary<string, string[]> _tagsByArticle = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string[]> _articlesByTag = new(StringComparer.Ordinal);

    private readonly Lock _links = new();

    /// <summary>Links one article to one tag; linking twice is one link.</summary>
    internal void Link(string articleId, string tagSlug)
    {
        lock (this._links)
        {
            Add(this._tagsByArticle, articleId, tagSlug);
            Add(this._articlesByTag, tagSlug, articleId);
        }
    }

    /// <summary>The slugs of this article's tags, in link order; empty for an article with none.</summary>
    internal IReadOnlyList<string> TagsOf(string articleId)
    {
        lock (this._links)
        {
            return this._tagsByArticle.TryGetValue(articleId, out string[]? slugs) ? slugs : [];
        }
    }

    /// <summary>The ids of this tag's articles, in link order; empty for a tag with none.</summary>
    internal IReadOnlyList<string> ArticlesOf(string tagSlug)
    {
        lock (this._links)
        {
            return this._articlesByTag.TryGetValue(tagSlug, out string[]? ids) ? ids : [];
        }
    }

    /// <summary>Makes these the article's tags, no more and no fewer.</summary>
    internal void ReplaceTagsOf(string articleId, IReadOnlyList<string> tagSlugs)
    {
        lock (this._links)
        {
            Replace(this._tagsByArticle, this._articlesByTag, articleId, tagSlugs);
        }
    }

    /// <summary>Makes these the tag's articles, no more and no fewer.</summary>
    internal void ReplaceArticlesOf(string tagSlug, IReadOnlyList<string> articleIds)
    {
        lock (this._links)
        {
            Replace(this._articlesByTag, this._tagsByArticle, tagSlug, articleIds);
        }
    }

    /// <summary>Drops every link the article had, on both sides.</summary>
    internal void RemoveArticle(string articleId) => ReplaceTagsOf(articleId, []);

    /// <summary>Drops every link the tag had, on both sides.</summary>
    internal void RemoveTag(string tagSlug) => ReplaceArticlesOf(tagSlug, []);

    private static void Add(Dictionary<string, string[]> index, string key, string value) =>
        index[key] = index.TryGetValue(key, out string[]? existing)
            ? existing.Contains(value, StringComparer.Ordinal) ? existing : [.. existing, value]
            : [value];

    private static void Remove(Dictionary<string, string[]> index, string key, string value)
    {
        if (index.TryGetValue(key, out string[]? existing))
        {
            string[] rest = existing.Where(v => !string.Equals(v, value, StringComparison.Ordinal)).ToArray();
            if (rest.Length == 0)
            {
                index.Remove(key);
            }
            else
            {
                index[key] = rest;
            }
        }
    }

    // Forward index gets the new list as posted, deduplicated; the reverse index
    // loses this key under every old value and gains it under every new one.
    private static void Replace(
        Dictionary<string, string[]> forward,
        Dictionary<string, string[]> reverse,
        string key,
        IReadOnlyList<string> values)
    {
        string[] next = values.Distinct(StringComparer.Ordinal).ToArray();
        string[] previous = forward.TryGetValue(key, out string[]? had) ? had : [];

        foreach (string gone in previous.Except(next, StringComparer.Ordinal))
        {
            Remove(reverse, gone, key);
        }

        foreach (string added in next.Except(previous, StringComparer.Ordinal))
        {
            Add(reverse, added, key);
        }

        if (next.Length == 0)
        {
            forward.Remove(key);
        }
        else
        {
            forward[key] = next;
        }
    }
}

internal sealed class TagStore(MemoryRelation relation, Func<string, bool>? articleExists) : MemoryStore<Tag>
{
    /// <summary>A store on its own: no article exists, so every posted id is refused.</summary>
    public TagStore()
        : this(new MemoryRelation(), null)
    {
    }

    protected override string KeyOf(Tag item) => item.Slug;

    protected override void AssignKey(Tag item, string key) => item.Slug = key;

    // A slug the caller supplied is the key (R9); a blank one is generated. The
    // form never carries the key, so a typed slug reaches here from code.
    protected override string KeyFor(Tag item) =>
        string.IsNullOrWhiteSpace(item.Slug) ? base.KeyFor(item) : item.Slug.Trim();

    protected override void BeforeSave(Tag item, Tag? existing)
    {
        if (existing is null && Has(item.Slug))
        {
            throw new ScaffoldRefusedException(Slugs.TakenMessage(item.Slug));
        }

        if (item.Articles.Any(id => articleExists?.Invoke(id) != true))
        {
            throw new ScaffoldRefusedException(Refusals.MissingChoice("Articles"));
        }

        relation.ReplaceArticlesOf(item.Slug, item.Articles);
        item.Articles = relation.ArticlesOf(item.Slug);
    }

    protected override void Load(Tag item) => item.Articles = relation.ArticlesOf(item.Slug);

    protected override void OnDelete(string key) => relation.RemoveTag(key);

    protected override string? Field(Tag item, string name) =>
        name == nameof(Tag.Name) ? item.Name : null;

    protected override IComparable? SortValue(Tag item, string name) =>
        name == nameof(Tag.Name) ? item.Name : item.Slug;
}

internal sealed class CategoryStore(Action<string>? onDelete) : MemoryStore<Category>
{
    public CategoryStore()
        : this(null)
    {
    }

    protected override string KeyOf(Category item) => item.Key;

    protected override void AssignKey(Category item, string key) => item.Key = key;

    // The articles that pointed at the deleted category now point at nothing
    // (R5), which is what ON DELETE SET NULL does on the database path.
    protected override void OnDelete(string key) => onDelete?.Invoke(key);

    protected override string? Field(Category item, string name) =>
        name == nameof(Category.Name) ? item.Name : null;

    protected override IComparable? SortValue(Category item, string name) =>
        name == nameof(Category.Name) ? item.Name : item.Key;
}
