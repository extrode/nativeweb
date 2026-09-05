using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

// The three instances of apps/sample this tier runs. Split out of
// SampleProcessFixture.cs on 2026-09-03 so the base class compiles on its own:
// tests/nativeweb.permissions.tests.e2e links that file rather than copying it,
// and JauntySampleFixture names SampleE2eDatabase, which lives in this tier and
// has no business travelling with the fixture machinery.

/// <summary>The demo with no database configured: the in-memory store.</summary>
public sealed class InMemorySampleFixture() : SampleProcessFixture(5347, null);

/// <summary>
/// The same binary with a connection string, so <c>IScaffoldStore&lt;Article&gt;</c>
/// resolves to the JauntyQ store.
/// </summary>
/// <remarks>
/// This fixture is the only place in the repo where JauntyQ-generated code runs
/// under NativeAOT. The integration tier exercises the same queries, but through a
/// JIT-compiled test host; a data layer that works there and trims away here would
/// fail for the first time in production.
/// </remarks>
public sealed class JauntySampleFixture() : SampleProcessFixture(5348, SampleE2eDatabase.ConnectionString);

/// <summary>
/// The same binary as Production sees it: no one-click sign-in, the passphrase form
/// instead, and a secret it would refuse to start without (spec 020).
/// </summary>
/// <remarks>
/// Over https, for the reason the base constructor gives. Port 5351: the sign-in tier
/// runs its own instance of the sample, and <c>dotnet test</c> runs the two tiers'
/// assemblies at the same time. That tier asks the operating system for its two ports
/// rather than naming them, so it cannot collide with this one.
/// </remarks>
public sealed class PassphraseSampleFixture() : SampleProcessFixture(
    5351,
    null,
    "Production",
    new Dictionary<string, string> { ["Sample__Admin__Passphrase"] = Secret },
    https: true)
{
    /// <summary>What the fixture hands the process, and what the tests type.</summary>
    public const string Secret = "correct horse battery staple";
}
