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


    /// <summary>The half a machine with IPv6 cannot reach by running the code as configured.</summary>
    /// <remarks>
    /// This host binds <c>::1</c>, so <c>LoopbackV6Bindable</c> is true here and the branch the
    /// justsmtp.com finding is about never executes on it. That was reported as untestable on
    /// 2026-09-05 and it was not: the decision was hard-wired into the allocator, so the only way
    /// in was a differently-configured machine. Passing the predicate makes both sides reachable
    /// from a test, and this asserts the one that matters -- a predicate that never accepts must
    /// exhaust the loop and say so, rather than returning a port the server cannot bind.
    /// </remarks>
    [Fact]
    public void FreePorts_WhenIPv6NeverAccepts_Throws()
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            static () => SignInProcessFixture.FreePorts(static _ => false));

        Assert.Contains("32 attempts", thrown.Message);
    }

    /// <summary>The other side of the same predicate: no IPv6 requirement, still a real port.</summary>
    /// <remarks>
    /// What a machine that cannot bind <c>::1</c> at all runs. It must not throw, and what it
    /// returns must still be bindable on 127.0.0.1 -- the whole point of the gate is that losing
    /// IPv6 costs the tier its IPv6 check, not its ports.
    /// </remarks>
    [Fact]
    public void FreePorts_WithNoIPv6Requirement_ReturnsABindablePort()
    {
        (int front, int back) = SignInProcessFixture.FreePorts(static _ => true);

        Assert.NotEqual(front, back);

        using TcpListener first = new(IPAddress.Loopback, front);
        using TcpListener second = new(IPAddress.Loopback, back);

        first.Start();
        second.Start();
    }

    /// <summary><c>CanBindV6</c> saying no, proved on a host where ::1 works.</summary>
    /// <remarks>
    /// The refusal path of the probe itself, reached by holding <c>::1</c> on a port and asking
    /// about that port. No special machine needed: an occupied address refuses a bind for the
    /// same reason a disabled one does, and this is what makes the <c>false</c> return something
    /// other than dead code here.
    /// </remarks>
    [Fact]
    public void CanBindV6_IsFalseWhenThatPortIsAlreadyHeldOnIPv6()
    {
        using TcpListener held = new(IPAddress.IPv6Loopback, 0);

        held.Start();

        int port = ((IPEndPoint)held.LocalEndpoint).Port;

        Assert.False(SignInProcessFixture.CanBindV6(port));
        Assert.True(SignInProcessFixture.CanBindV6(0));
    }
}
