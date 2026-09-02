using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// Runs <see cref="SampleTestDatabase.SweepLeakedFeedRowsAsync"/> once per test
/// class per process, before the first feed test inserts anything.
/// </summary>
public sealed class LeakedFeedRowsSweep : IAsyncLifetime
{
    public Task InitializeAsync() => SampleTestDatabase.SweepLeakedFeedRowsAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
