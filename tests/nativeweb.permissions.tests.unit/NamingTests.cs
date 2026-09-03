using NativeWeb.Permissions.Generator;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

public sealed class NamingTests
{
    [Theory]
    [InlineData("sample", "Article", "view", "sample:article.view")]
    [InlineData("sample", "Article", "change", "sample:article.change")]
    [InlineData("my.app", "BlogPost", "delete", "my.app:blogpost.delete")]
    [InlineData("sample", "Article", "publish_draft", "sample:article.publish_draft")]
    public void Compose_JoinsScopeModelAndAction(string scope, string type, string action, string expected) =>
        Assert.Equal(expected, PermissionNaming.Compose(scope, type, action));

    [Fact]
    public void Compose_SeparatesScopeWithAColonSoADottedAssemblyNameIsUnambiguous()
    {
        string name = PermissionNaming.Compose("my.app", "Article", "view");

        Assert.Equal("my.app", name[..name.IndexOf(':', StringComparison.Ordinal)]);
        Assert.Equal("article.view", name[(name.IndexOf(':', StringComparison.Ordinal) + 1)..]);
    }

    [Theory]
    [InlineData("view", "View")]
    [InlineData("change", "Change")]
    [InlineData("publish_draft", "PublishDraft")]
    [InlineData("a_b_c", "ABC")]
    public void ConstantName_PascalCasesEachUnderscoreSeparatedWord(string action, string expected) =>
        Assert.Equal(expected, PermissionNaming.ConstantName(action));

    [Fact]
    public void ConstantsClassName_IsTheTypeNamePlusPermissions() =>
        Assert.Equal("ArticlePermissions", PermissionNaming.ConstantsClassName("Article"));

    [Theory]
    [InlineData("sample", true)]
    [InlineData("my.app", true)]
    [InlineData("a1_b", true)]
    [InlineData("Sample", false)]
    [InlineData("1sample", false)]
    [InlineData(".sample", false)]
    [InlineData("sam ple", false)]
    [InlineData("sam:ple", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidScope_AcceptsLowerAlnumDotUnderscoreStartingWithALetter(string? scope, bool expected) =>
        Assert.Equal(expected, PermissionNaming.IsValidScope(scope));

    [Theory]
    [InlineData("view", true)]
    [InlineData("publish_draft", true)]
    [InlineData("archive2", true)]
    [InlineData("Publish", false)]
    [InlineData("publish.draft", false)]
    [InlineData("publish:draft", false)]
    [InlineData("2archive", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidAction_RejectsTheTwoSeparators(string? action, bool expected) =>
        Assert.Equal(expected, PermissionNaming.IsValidAction(action));

    [Fact]
    public void CoreActions_AreTheFour() =>
        Assert.Equal(["view", "add", "change", "delete"], PermissionNaming.CoreActions);

    [Theory]
    [InlineData("Sample", "sample")]
    [InlineData("My.App", "my.app")]
    public void DefaultScope_LowerCasesTheAssemblyName(string assembly, string expected) =>
        Assert.Equal(expected, PermissionNaming.DefaultScope(assembly));

    [Theory]
    [InlineData("1sample")]
    [InlineData("sam ple")]
    [InlineData("")]
    [InlineData(null)]
    public void DefaultScope_IsNullWhenTheAssemblyNameCannotFormOne(string? assembly) =>
        Assert.Null(PermissionNaming.DefaultScope(assembly));

    [Fact]
    public void ModelSegment_IsNotPluralised() =>
        Assert.Equal("article", PermissionNaming.ModelSegment("Article"));
}
