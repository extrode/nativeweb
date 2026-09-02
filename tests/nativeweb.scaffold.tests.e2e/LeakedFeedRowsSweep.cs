using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// Runs <see cref="SampleE2eDatabase.SweepLeakedFeedRowsAsync"/> once per test
/// class per process, before the first feed test inserts anything.
/// </summary>
public sealed class LeakedFeedRowsSweep : IAsyncLifetime
{
    public Task InitializeAsync() => SampleE2eDatabase.SweepLeakedFeedRowsAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
