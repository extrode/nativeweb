using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

public sealed class LeakedFeedRowsSweepTests
{
    [Fact]
    public async Task OnlyAFutureDatedRowMintedLongEnoughAgoIsSwept()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            // Minted two hours ago by a test using the largest offset any feed test adds.
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-leaked", $"{prefix}-leaked", "xunit", "body", published: true, createdAt: future.AddHours(-1));
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-live", $"{prefix}-live", "xunit", "body", published: true, createdAt: future);
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-past", $"{prefix}-past", "xunit", "body", published: true, createdAt: DateTime.UtcNow.AddHours(-2));

            await SampleTestDatabase.SweepLeakedFeedRowsAsync();

            Assert.Null(await SampleTestDatabase.FindByTitleAsync($"{prefix}-leaked"));
            Assert.NotNull(await SampleTestDatabase.FindByTitleAsync($"{prefix}-live"));
            Assert.NotNull(await SampleTestDatabase.FindByTitleAsync($"{prefix}-past"));
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }
}
