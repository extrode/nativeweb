using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// Runs <see cref="SampleTestDatabase.SweepLeakedFeedRowsAsync"/> once per test
/// class per process, before the first feed test inserts anything.
/// </summary>
public sealed class LeakedFeedRowsSweep : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(SampleTestDatabase.SweepLeakedFeedRowsAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
