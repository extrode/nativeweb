using System.Net;
using System.Net.Sockets;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// The port every fixture in this tier binds, without a fixture: no publish, no browser.
/// </summary>
/// <remarks>
/// Outside the e2e collection on purpose, so this costs milliseconds rather than waiting on a
/// NativeAOT publish. It is not a competitor for a fixture's port: <c>ScaffoldE2eFixture</c>
/// sets <c>DisableParallelization</c>, which stops that collection running alongside anything
/// else in the assembly, so this class and a fixture's lifetime never overlap. The sign-in tier
/// tried the other arrangement and it made the fact worse -- a broken allocator failed with the
/// fixture's "Hosting failed to start" instead of the assertion that names the defect.
/// <para>
/// The two loopback addresses specifically, not <c>IPv6Any</c> with <c>DualMode</c>. The first
/// shape of this test bound the wildcard and passed while <c>FreePort</c> was hardcoded to
/// return an occupied 5348: Windows lets a wildcard bind coexist with a listener already on
/// 127.0.0.1. Kestrel is given <c>localhost</c>, which resolves to 127.0.0.1 and ::1 and binds
/// each, so binding the wildcard asserted something the fixture never does.
/// </para>
/// </remarks>
public sealed class PortAllocationTests
{
    [Fact]
    public void FreePort_ReturnsAPortThatCanThenBeBound()
    {
        int port = SampleProcessFixture.FreePort();

        Assert.NotEqual(0, port);

        using TcpListener v4 = new(IPAddress.Loopback, port);
        using TcpListener v6 = new(IPAddress.IPv6Loopback, port);

        v4.Start();
        v6.Start();
    }
}
