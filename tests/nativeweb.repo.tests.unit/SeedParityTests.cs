using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The demo dataset is written twice -- once for the in-memory store and once for
/// the database -- and the two must stay the same dataset.
/// </summary>
/// <remarks>
/// <para>
/// Plan 009 grew the sample's demo articles from three to eight and, in doing so,
/// made an asymmetry visible that had existed since the database path was added:
/// <c>Seed.Fill</c> in <c>apps/sample/Program.cs</c> seeded the memory store and
/// nothing seeded PostgreSQL, so the same binary served three articles or none
/// depending only on whether a connection string was set.
/// </para>
/// <para>
/// The fix is one dataset in two files, which trades a missing-content bug for a
/// drift bug: an article added to one and not the other, or a status changed on one
/// side, is invisible until someone runs both stores and compares by eye. Neither
/// tier can catch it -- the sample's unit tests only ever see the memory seed, and
/// the integration tier's database has fifty thousand rows from paging runs, so it
/// cannot assert an exact set. This is a repository fact, so it is asserted here
/// with the rest of them.
/// </para>
/// <para>
/// Field-by-field rather than by row count: a count agrees while a publication date
/// differs, and the publication date is the field the public order depends on.
/// </para>
/// </remarks>
public sealed class SeedParityTests
{
    private sealed record SeedArticle(
        string Id,
        string Slug,
        string Title,
        string? Author,
        string Status,
        string? PublishedAt,
        string? CategoryKey,
        int ViewCount);

    [Fact]
    public void TheMigrationAndTheMemorySeedDeclareTheSameArticles()
    {
        List<SeedArticle> sql = FromMigration();
        List<SeedArticle> memory = FromProgram();

        Assert.NotEmpty(sql);
        Assert.Equal(sql.Count, memory.Count);
        Assert.Equal(sql, memory);
    }

    [Fact]
    public void TheMigrationAndTheMemorySeedDeclareTheSameTagLinks()
    {
        Assert.Equal(TagLinksFromMigration(), TagLinksFromProgram());
    }

    // Every article in the migration's single INSERT, read as the tuple the memory
    // seed also declares. The regex is anchored on the leading ('aN', so a row that
    // stops matching drops out of the list and fails the count rather than being
    // compared as something else.
    private static List<SeedArticle> FromMigration()
    {
        string sql = ReadRepoFile("db/sample/migrations/0005_seed_demo_content.sql");
        string articles = Between(sql, "INSERT INTO articles", "ON CONFLICT (id) DO NOTHING;");

        var rows = new List<SeedArticle>();
        foreach (Match row in Regex.Matches(
            articles,
            @"\('(?<id>a\d+)',\s*'(?<title>(?:[^']|'')*)',\s*(?<author>NULL|'(?:[^']|'')*'),\s*(?<body>.*?),\s*(?<views>\d+),\s*'[^']*',\s*'(?<slug>[^']*)',\s*'(?<status>[^']*)',\s*(?<published>NULL|'[^']*'),\s*(?<category>NULL|'[^']*')\)",
            RegexOptions.Singleline))
        {
            rows.Add(new SeedArticle(
                row.Groups["id"].Value,
                row.Groups["slug"].Value,
                Unquote(row.Groups["title"].Value) ?? "",
                Unquote(row.Groups["author"].Value),
                row.Groups["status"].Value,
                Unquote(row.Groups["published"].Value),
                Unquote(row.Groups["category"].Value),
                int.Parse(row.Groups["views"].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    // Every articles.Seed(new Article { ... }) block in Seed.Fill. Status is written
    // as ArticleStatus.Published in C# and 'published' in SQL, and PublishedAt as
    // new DateOnly(y, m, d) against a date literal, so both are normalised to the
    // SQL spelling here -- the comparison is of what the row means, not of how each
    // language spells it.
    private static List<SeedArticle> FromProgram()
    {
        string program = ReadRepoFile("apps/sample/Program.cs");
        string fill = Between(program, "internal static void Fill(", "\n    }");

        var rows = new List<SeedArticle>();
        foreach (Match block in Regex.Matches(fill, @"articles\.Seed\(new Article\s*\{(?<body>.*?)\}\);", RegexOptions.Singleline))
        {
            string body = block.Groups["body"].Value;
            rows.Add(new SeedArticle(
                Field(body, "Id") ?? "",
                Field(body, "Slug") ?? "",
                Field(body, "Title") ?? "",
                Field(body, "Author"),
                (Raw(body, "Status") ?? "ArticleStatus.Draft").Replace("ArticleStatus.", "", StringComparison.Ordinal).ToLowerInvariant(),
                DateOf(Raw(body, "PublishedAt")),
                Field(body, "CategoryKey"),
                int.Parse(Raw(body, "ViewCount") ?? "0", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static List<string> TagLinksFromMigration()
    {
        string sql = ReadRepoFile("db/sample/migrations/0005_seed_demo_content.sql");
        string links = Between(sql, "INSERT INTO article_tags", "ON CONFLICT DO NOTHING;");

        return Regex.Matches(links, @"\('(?<article>[^']+)',\s*'(?<tag>[^']+)'\)")
            .Select(m => $"{m.Groups["article"].Value}:{m.Groups["tag"].Value}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> TagLinksFromProgram()
    {
        string program = ReadRepoFile("apps/sample/Program.cs");
        string fill = Between(program, "internal static void Fill(", "\n    }");

        return Regex.Matches(fill, @"relation\.Link\(""(?<article>[^""]+)"",\s*""(?<tag>[^""]+)""\)")
            .Select(m => $"{m.Groups["article"].Value}:{m.Groups["tag"].Value}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string? Field(string body, string name)
    {
        string? raw = Raw(body, name);
        return raw is null or "null" ? null : raw.Trim('"');
    }

    private static string? Raw(string body, string name)
    {
        Match match = Regex.Match(body, $@"\b{name}\s*=\s*(?<value>""(?:[^""\\]|\\.)*""|(?:new\s+)?[A-Za-z0-9_.]+(?:\([^)]*\))?|null)");
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? DateOf(string? expression)
    {
        Match match = Regex.Match(expression ?? "", @"new DateOnly\((?<y>\d+),\s*(?<m>\d+),\s*(?<d>\d+)\)");
        return match.Success
            ? $"{match.Groups["y"].Value}-{int.Parse(match.Groups["m"].Value, System.Globalization.CultureInfo.InvariantCulture):00}-{int.Parse(match.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture):00}"
            : null;
    }

    private static string? Unquote(string value) =>
        value is "NULL" ? null : value.Trim('\'').Replace("''", "'", StringComparison.Ordinal);

    private static string Between(string text, string open, string close)
    {
        int start = text.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{open}' is no longer in the file this test parses.");
        int end = text.IndexOf(close, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{close}' is no longer in the file this test parses.");

        return text[start..end];
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NativeWeb.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }
}
