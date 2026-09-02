using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

public sealed class SlugTests
{
    [Theory]
    [InlineData("Hello, World! (Again)", "hello-world-again")]
    [InlineData("Lean Native Web", "lean-native-web")]
    [InlineData("  spaced   out  ", "spaced-out")]
    [InlineData("MiXeD CaSe 123", "mixed-case-123")]
    [InlineData("--already-a-slug--", "already-a-slug")]
    [InlineData("Ünïcödé façade", "n-c-d-fa-ade")]
    [InlineData("日本語だけ", "article")]
    [InlineData("!!!", "article")]
    [InlineData("", "article")]
    [InlineData(null, "article")]
    public void Derive_MakesAnAddressOutOfATitle(string? title, string expected) =>
        Assert.Equal(expected, Slugs.Derive(title));

    [Fact]
    public void Derive_CapsTheLengthWithoutEndingOnAHyphen()
    {
        string title = string.Join(' ', Enumerable.Repeat("abcdefghij", 20));

        string slug = Slugs.Derive(title);

        Assert.Equal(string.Join('-', Enumerable.Repeat("abcdefghij", 7)) + "-abc", slug);
        Assert.Equal(Slugs.MaxDerivedLength, slug.Length);
        Assert.True(Slugs.IsValid(slug));
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("hello-world-again", true)]
    [InlineData("2026-09-02", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Hello", false)]
    [InlineData("hello world", false)]
    [InlineData("hello--world", false)]
    [InlineData("-hello", false)]
    [InlineData("hello-", false)]
    [InlineData("hello.world", false)]
    [InlineData("héllo", false)]
    [InlineData("../admin", false)]
    public void IsValid_AcceptsOnlyTheAlphabet(string? candidate, bool expected) =>
        Assert.Equal(expected, Slugs.IsValid(candidate));

    [Fact]
    public void Unique_TakesTheWantedSlugWhenItIsFree()
    {
        Assert.Equal("post", Slugs.Unique("post", _ => false));
    }

    [Fact]
    public void Unique_CountsUpFromTwoPastEveryTakenOne()
    {
        string[] taken = ["post", "post-2", "post-3"];

        Assert.Equal("post-4", Slugs.Unique("post", taken.Contains));
    }

    [Fact]
    public void TakenMessage_NamesTheSlug()
    {
        Assert.Equal("Slug 'post-2' is already used.", Slugs.TakenMessage("post-2"));
    }
}
