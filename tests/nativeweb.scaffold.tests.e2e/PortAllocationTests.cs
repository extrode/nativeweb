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


}
