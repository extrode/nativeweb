using System.Net;
using System.Net.Sockets;

using Xunit;

namespace NativeWeb.SignIn.Tests.E2e;

/// <summary>
/// The port pair the fixture binds, without the fixture: no publish, no browser (spec 016 R26).
/// </summary>
/// <remarks>
/// Outside the e2e collection on purpose, so this costs milliseconds rather than waiting on a
/// NativeAOT publish and a browser. It is not a competitor for the fixture's ports:
/// <c>SignInE2eFixture</c> sets <c>DisableParallelization</c>, which stops that collection
/// running alongside anything else in the assembly, so this class and the fixture's lifetime
/// never overlap. Joining the collection was tried and made the fact worse -- a broken
/// allocator then failed it with the fixture's "Hosting failed to start" instead of the
/// assertion that names the defect.
/// </remarks>
public sealed class PortAllocationTests
{
    [Fact]
    public void FreePorts_AreDistinctAndCanBothBeBoundAtOnce()
    {
        (int front, int back) = SignInProcessFixture.FreePorts();

        Assert.NotEqual(front, back);

        using TcpListener first = new(IPAddress.Loopback, front);
        using TcpListener second = new(IPAddress.Loopback, back);

        first.Start();
        second.Start();

        if (Socket.OSSupportsIPv6)
        {
            using TcpListener firstV6 = new(IPAddress.IPv6Loopback, front);
            using TcpListener secondV6 = new(IPAddress.IPv6Loopback, back);

            firstV6.Start();
            secondV6.Start();
        }
    }
}
