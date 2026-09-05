using System.Net;
using System.Net.Sockets;

using Xunit;

namespace NativeWeb.SignIn.Tests.E2e;

/// <summary>
/// The port pair the fixture binds, without the fixture: no publish, no browser (spec 016 R26).
/// </summary>
public sealed class PortAllocationTests
{
    [Fact]
    public void FreeLoopbackPorts_AreDistinctAndCanBeBound()
    {
        (int front, int back) = SignInProcessFixture.FreeLoopbackPorts();

        Assert.NotEqual(front, back);

        foreach (int port in new[] { front, back })
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
    }
}
