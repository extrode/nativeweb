using System.Text;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

public sealed class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\r\nbreak", "\"line\r\nbreak\"")]
    [InlineData("tab\there", "tab\there")]
    [InlineData("\tcmd", "\tcmd")]
    [InlineData("\t=cmd", "\"'\t=cmd\"")]
    [InlineData("\r=cmd", "\"'\r=cmd\"")]
    [InlineData("=SUM(A1)", "\"'=SUM(A1)\"")]
    [InlineData("+1+1", "\"'+1+1\"")]
    [InlineData("@cmd", "\"'@cmd\"")]
    [InlineData("-cmd", "\"'-cmd\"")]
    [InlineData(" =SUM(A1)", "\"' =SUM(A1)\"")]
    [InlineData("​=SUM(A1)", "\"'​=SUM(A1)\"")]
    [InlineData("‮=SUM(A1)", "\"'‮=SUM(A1)\"")]
    [InlineData("\n=SUM(A1)", "\"'\n=SUM(A1)\"")]
    [InlineData("\u2028=SUM(A1)", "\"'\u2028=SUM(A1)\"")]
    [InlineData("\u2029=SUM(A1)", "\"'\u2029=SUM(A1)\"")]
    [InlineData("-5", "-5")]
    [InlineData("-5.25", "-5.25")]
    [InlineData("+7", "+7")]
    [InlineData("-1,000", "\"-1,000\"")]
    [InlineData("   ", "   ")]
    public void Field_QuotesAndNeutralisesAsRfc4180AndOwaspSay(string? value, string expected)
    {
        Assert.Equal(expected, CsvWriter.Field(value));
    }

    [Fact]
    public void Write_RefusesARowWhoseCellCountIsNotTheHeaderCount()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            CsvWriter.Write(["a", "b"], [["1", "2"], ["3"]]));

        Assert.StartsWith("Row 2 has 1 cells for 2 headers.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EndsEveryRowInCrlfAndStartsWithTheHeader()
    {
        string csv = CsvWriter.Write(["id", "title"], [["a1", "One"], ["a2", "Two, too"]]);

        Assert.Equal("id,title\r\na1,One\r\na2,\"Two, too\"\r\n", csv);
    }

    [Fact]
    public async Task Csv_DeclaresTypeLengthAndNoByteOrderMark()
    {
        (HttpResponse response, byte[] body) = await FormatResultsTests.ExecuteAsync(
            FormatResults.Csv(["title"], [[FormatResultsTests.HostileTitle]]));

        Assert.Equal("text/csv; charset=utf-8", response.ContentType);
        Assert.Equal(body.Length, response.ContentLength);
        Assert.Equal((byte)'t', body[0]);

        string text = Encoding.UTF8.GetString(body);
        Assert.Equal("title\r\n\"<b>\"\"Q&A\"\" & Co,</b>\nline two \U0001F600\"\r\n", text);
    }
}
