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

        if (SignInProcessFixture.LoopbackV6Bindable)
        {
            using TcpListener firstV6 = new(IPAddress.IPv6Loopback, front);
            using TcpListener secondV6 = new(IPAddress.IPv6Loopback, back);

            firstV6.Start();
            secondV6.Start();
        }
    }

    /// <remarks>
    /// <para>
    /// The direction this can prove on the machine it runs on. A host where ::1 is unbindable is
    /// not available here, so the disabled-by-policy case the guard exists for cannot be
    /// exercised; what can be is that the cached answer matches what a bind does right now, in
    /// either direction. Reverting the helper to <c>Socket.OSSupportsIPv6</c> on a host where the
    /// two disagree turns this red, which is the whole class of bug reported.
    /// </para>
    /// <para>
    /// Falsified by returning a constant from the helper: <c>false</c> fails here, since ::1 does
    /// bind on this machine. <c>true</c> does not fail here, and cannot -- that is the half no
    /// test on this host can reach, and it is stated rather than papered over.
    /// </para>
    /// </remarks>
    [Fact]
    public void LoopbackV6Bindable_MatchesWhatTheMachineDoes()
    {
        bool bound;

        try
        {
            using TcpListener v6 = new(IPAddress.IPv6Loopback, 0);

            v6.Start();

            bound = true;
        }
        catch (SocketException)
        {
            bound = false;
        }

        Assert.Equal(bound, SignInProcessFixture.LoopbackV6Bindable);
    }

}
