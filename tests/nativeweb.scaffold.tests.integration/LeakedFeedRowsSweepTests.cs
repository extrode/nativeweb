using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

public sealed class LeakedFeedRowsSweepTests
{
    [Fact]
    public async Task OnlyAFutureDatedFeedRowMintedLongEnoughAgoIsSwept()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];
        string other = $"kq-{Guid.NewGuid():N}"[..14];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-leaked", $"{prefix}-leaked", "xunit", "body", published: false, createdAt: future.AddHours(-1));
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-live", $"{prefix}-live", "xunit", "body", published: false, createdAt: future);
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-past", $"{prefix}-past", "xunit", "body", published: false, createdAt: DateTime.UtcNow.AddHours(-2));
            await SampleTestDatabase.InsertAsync(
                $"{other}-leaked", $"{other}-leaked", "xunit", "body", published: false, createdAt: future.AddHours(-1));

            await SampleTestDatabase.SweepLeakedFeedRowsAsync();

            Assert.Null(await SampleTestDatabase.FindByTitleAsync($"{prefix}-leaked"));
            Assert.NotNull(await SampleTestDatabase.FindByTitleAsync($"{prefix}-live"));
            Assert.NotNull(await SampleTestDatabase.FindByTitleAsync($"{prefix}-past"));
            Assert.NotNull(await SampleTestDatabase.FindByTitleAsync($"{other}-leaked"));
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
            await SampleTestDatabase.DeleteByIdPrefixAsync(other);
        }
    }
}
