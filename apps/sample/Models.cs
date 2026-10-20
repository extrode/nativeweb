using NativeWeb.Permissions;
using NativeWeb.Scaffold;

// Spec 024 R2. Without this the scope is the assembly name lower-cased, which here
// would be "nativeweb.sample" and would put the whole framework's name into every
// grant row. One word, and it is the product's, so a grant reads as
// sample:article.change.
[assembly: PermissionScope("sample")]

namespace NativeWeb.Sample;

/// <summary>Where an article is in its life. Only Published is visible to the public site.</summary>
public enum ArticleStatus
{
    Draft,
    Published,
    Archived,
}

/// <summary>
/// The whole of the admin UI for articles. There is no controller, no view, no
/// form binding and no route table anywhere in this project -- the attributes
/// below are the entire input to the generator.
/// </summary>
[Scaffold(Display = "Articles", Route = "articles")]
[Auth(Role = "admin")]
[Permissions]
public sealed class Article
{
    [ScaffoldKey]
    public string Id { get; set; } = "";

    [ScaffoldList]
    [ScaffoldSearch]
    [ScaffoldSort]
    public string Title { get; set; } = "";

    // The public address (spec 022). Nullable so the generated form does not
    // require it: blank on create derives from the title, blank on edit keeps
    // what the row has. The store owns both rules and refuses a duplicate or an
    // invalid one through ScaffoldRefusedException; see Slugs.cs.
    [ScaffoldList]
    public string? Slug { get; set; }

    // The nullable one, and sortable on purpose: a nullable sorted column is where
    // keyset paging goes wrong quietly, so the reference app has to demonstrate it
    // rather than avoid it.
    [ScaffoldList(Label = "Author")]
    [ScaffoldSearch]
    [ScaffoldSort]
    public string? Author { get; set; }

    [ScaffoldMultiline]
    [ScaffoldSearch]
    public string Body { get; set; } = "";

    // An enum renders as a dropdown of its members (spec 023 R3). Draft is the
    // default, which is what a new row gets when the form says nothing.
    [ScaffoldList]
    public ArticleStatus Status { get; set; }

    // A date with no time renders as a date picker (R4); nullable, so a row can
    // have no publication date and the public order falls back to CreatedAt.
    [ScaffoldList(Label = "Published")]
    public DateOnly? PublishedAt { get; set; }

    // The key of another scaffolded model renders as a dropdown of that model's
    // rows with a "(none)" first (R5). Nullable, because "none" is a valid pick.
    [ScaffoldList(Label = "Category")]
    [ScaffoldReference(typeof(Category))]
    public string? CategoryKey { get; set; }

    // The keys of many rows of another model render as a checkbox list (R1).
    // The store owns the link table; the form posts the whole ticked set.
    [ScaffoldRelation(typeof(Tag))]
    public IReadOnlyList<string> Tags { get; set; } = [];

    // Read-only so the form never posts it back (R8): a visit counted while the
    // edit form was open used to be overwritten by the count the form carried.
    [ScaffoldList(Label = "Views")]
    [ScaffoldReadOnly]
    public int ViewCount { get; set; }

    // Stamped by the store on insert, never by a form. Marking it read-only is
    // what keeps an input for it out of the emitted HTML.
    [ScaffoldList(Label = "Created")]
    [ScaffoldReadOnly]
    [ScaffoldSort]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// The same thing with no <c>[Auth]</c>, so the generated routes are open. That is
/// a choice the consumer makes by omission, and the difference between the two
/// models is visible in the emitted <c>MapTagScaffold</c>.
/// </summary>
[Scaffold]
public sealed class Tag
{
    [ScaffoldKey]
    public string Slug { get; set; } = "";

    [ScaffoldList]
    [ScaffoldSearch]
    [ScaffoldSort]
    public string Name { get; set; } = "";

    [ScaffoldList]
    public string? Color { get; set; }

    // The other side of Article.Tags, as a multi-select (R2), so the two controls
    // can be compared on one relation. A link made on either side shows on both.
    [ScaffoldRelation(typeof(Article), Control = ScaffoldRelationControl.MultiSelect)]
    public IReadOnlyList<string> Articles { get; set; } = [];
}

/// <summary>
/// The target of <see cref="Article.CategoryKey"/> (R5): the smallest model that
/// can be referred to, a key and a name.
/// </summary>
[Scaffold(Display = "Categories", Route = "categories")]
[Auth(Role = "admin")]
public sealed class Category
{
    [ScaffoldKey]
    public string Key { get; set; } = "";

    [ScaffoldList]
    [ScaffoldSearch]
    [ScaffoldSort]
    public string Name { get; set; } = "";
}

/// <summary>
/// The refusal a store gives for a posted key that names no row (R7), the same
/// words on both store paths so a test written against one holds on the other.
/// </summary>
internal static class Refusals
{
    internal static string MissingChoice(string label) => label + " has a choice that does not exist.";
}
