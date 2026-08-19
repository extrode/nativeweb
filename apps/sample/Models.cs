using NativeWeb.Scaffold;

namespace NativeWeb.Sample;

/// <summary>
/// The whole of the admin UI for articles. There is no controller, no view, no
/// form binding and no route table anywhere in this project -- the attributes
/// below are the entire input to the generator.
/// </summary>
[Scaffold(Display = "Articles", Route = "articles")]
[Auth(Role = "admin")]
public sealed class Article
{
    [ScaffoldKey]
    public string Id { get; set; } = "";

    [ScaffoldList]
    [ScaffoldSearch]
    public string Title { get; set; } = "";

    [ScaffoldList(Label = "Author")]
    [ScaffoldSearch]
    public string? Author { get; set; }

    [ScaffoldMultiline]
    [ScaffoldSearch]
    public string Body { get; set; } = "";

    [ScaffoldList]
    public bool Published { get; set; }

    [ScaffoldList(Label = "Views")]
    public int ViewCount { get; set; }

    // Stamped by the store on insert, never by a form. Marking it read-only is
    // what keeps an input for it out of the emitted HTML.
    [ScaffoldList(Label = "Created")]
    [ScaffoldReadOnly]
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
    public string Name { get; set; } = "";

    [ScaffoldList]
    public string? Color { get; set; }
}
