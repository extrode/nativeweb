using System.Net;
using System.Net.Sockets;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// The allocator every fixture in this tier draws from, without a fixture: no publish,
/// no browser.
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
/// each, so binding the wildcard asserted something the fixture never does. The same
/// measurement is why <c>FreePort</c> itself probes the pair.
/// </para>
/// <para>
/// ::1 is gated on <see cref="SampleProcessFixture.LoopbackV6Bindable"/>, the same gate the
/// allocator uses, so the test cannot be stricter than the thing it stands in for. It was
/// <c>Socket.OSSupportsIPv6</c> until 2026-09-05: that flag reports the stack, not whether ::1
/// binds, and the fixture's own remarks record why the difference matters.
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

        v4.Start();

        if (SampleProcessFixture.LoopbackV6Bindable)
        {
            using TcpListener v6 = new(IPAddress.IPv6Loopback, port);

            v6.Start();
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

        Assert.Equal(bound, SampleProcessFixture.LoopbackV6Bindable);
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
    public void FreePort_WhenIPv6NeverAccepts_Throws()
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            static () => SampleProcessFixture.FreePort(static _ => false));

        Assert.Contains("32 attempts", thrown.Message);
    }

    /// <summary>The other side of the same predicate: no IPv6 requirement, still a real port.</summary>
    /// <remarks>
    /// What a machine that cannot bind <c>::1</c> at all runs. It must not throw, and what it
    /// returns must still be bindable on 127.0.0.1 -- the whole point of the gate is that losing
    /// IPv6 costs the tier its IPv6 check, not its ports.
    /// </remarks>
    [Fact]
    public void FreePort_WithNoIPv6Requirement_ReturnsABindablePort()
    {
        int port = SampleProcessFixture.FreePort(static _ => true);

        using TcpListener v4 = new(IPAddress.Loopback, port);

        v4.Start();
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

        Assert.False(SampleProcessFixture.CanBindV6(port));
        Assert.True(SampleProcessFixture.CanBindV6(0));
    }
}
