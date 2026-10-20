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
    /// <para>
    /// This host binds <c>::1</c>, so <c>LoopbackV6Bindable</c> is true here and the branch the
    /// justsmtp.com finding is about never executes on it. That was reported as untestable on
    /// 2026-09-05 and it was not: the decision was hard-wired into the allocator, so the only way
    /// in was a differently-configured machine. Passing the predicate makes the loop's handling of
    /// a refusing <c>::1</c> assertable here, which is what this does: a predicate that never
    /// accepts must exhaust the loop and say so, rather than returning a port the server cannot
    /// bind.
    /// </para>
    /// <para>
    /// It does not make everything testable, and an earlier version of these remarks said it did.
    /// The selection itself -- the ternary that picks the real probe or a no-op -- still runs only
    /// its true arm on a host that binds <c>::1</c>. The seam moved the untestable part from a
    /// loop body into a one-line ternary rather than removing it. Fable's finding, 2026-09-05.
    /// </remarks>
    [Fact]
    public void FreePort_WhenIPv6NeverAccepts_Throws()
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            static () => SampleProcessFixture.FreePort(static _ => false));

        Assert.Contains("32 attempts", thrown.Message);
    }

    /// <summary><c>CanBindV6</c> saying no, proved on a host where ::1 works.</summary>
    /// <remarks>
    /// <para>
    /// The refusal path of the probe itself, reached by holding <c>::1</c> on a port and asking
    /// about that port. No special machine needed on a host that binds <c>::1</c>: an occupied
    /// address refuses a bind for the same reason a disabled one does, and this is what makes the
    /// <c>false</c> return something other than dead code here.
    /// </para>
    /// <para>
    /// Returns early where <c>::1</c> cannot be bound, because its own arrange step binds <c>::1</c>
    /// and would throw before reaching an assertion -- on the one host class this whole change
    /// exists for, and with the "forbidden by its access permissions" text the change is meant to
    /// stop producing. A test that fails where the allocator it stands in for succeeds is stricter
    /// than the thing it tests, which is the property this file must not have.
    /// </remarks>
    [Fact]
    public void CanBindV6_IsFalseWhenThatPortIsAlreadyHeldOnIPv6()
    {
        if (!SampleProcessFixture.LoopbackV6Bindable)
        {
            return;
        }

        using TcpListener held = new(IPAddress.IPv6Loopback, 0);

        held.Start();

        int port = ((IPEndPoint)held.LocalEndpoint).Port;

        Assert.False(SampleProcessFixture.CanBindV6(port));
        Assert.True(SampleProcessFixture.CanBindV6(0));
    }
}
