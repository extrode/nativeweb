namespace NativeWeb.Web;

/// <summary>
/// Serves <c>/readyz</c> from a short-lived cached answer so the probe's cost is
/// bounded no matter how often it is called.
/// </summary>
/// <remarks>
/// <para>
/// /readyz is the only unauthenticated route that reaches PostgreSQL: every hit
/// takes a pooled connection and runs <c>SELECT 1</c>, so an unbounded one lets
/// any anonymous client spend the pool from a single address.
/// </para>
/// <para>
/// A rate limit was the obvious fix and is the wrong one. Partitioning is by
/// RemoteIpAddress, and until the forwarded-headers known proxies are configured
/// every client collapses into the proxy's single partition — which is the same
/// partition the load balancer's own probe lands in. An attacker spending that
/// budget would make the next real probe return 429, the balancer would read a
/// non-2xx as unhealthy, and a working instance would leave rotation. That trades
/// a bounded resource cost for a remote off-switch.
/// </para>
/// <para>
/// Caching removes the amplification at the source instead: concurrent and
/// repeated probes collapse onto one database round trip per window, every
/// caller still gets a real answer, and nobody ever sees a 429. One second is
/// far below any sane probe interval, so the staleness is never observable to a
/// balancer, and a genuine outage is still reported within a second of it
/// starting.
/// </para>
/// <para>Skeleton copy -- the product owns this file after stamping.</para>
/// </remarks>
/// <param name="window">How long one probe's answer is served for.</param>
/// <param name="probe">Runs the actual readiness check.</param>
/// <param name="clock">
/// Elapsed time from an arbitrary fixed origin, and it MUST be monotonic —
/// <c>Environment.TickCount64</c>, not <c>DateTimeOffset.UtcNow</c>. A wall clock
/// steps backwards on an NTP correction or a VM resume, and a backward step
/// leaves <c>clock() &lt; _expiresAt</c> true for the whole size of that step: the
/// cache would keep answering "ready" for minutes after the database had gone,
/// which is exactly what the window and the clamp on it exist to prevent. Hence
/// TimeSpan-from-an-origin rather than a DateTimeOffset, so a caller cannot
/// supply a wall clock without noticing that it does not fit.
/// </param>
public sealed class ReadinessCache(TimeSpan window, Func<Task<bool>> probe, Func<TimeSpan> clock)
{
    private readonly Lock _sync = new();
    private Task<bool>? _inFlight;
    private bool _ready;
    private TimeSpan _expiresAt = TimeSpan.MinValue;

    /// <remarks>
    /// Takes no CancellationToken on purpose. The obvious signature forwards the
    /// caller's, and the caller here is a request, so the token is
    /// HttpContext.RequestAborted — a shared operation must not observe one
    /// participant's lifetime. A client that connected and disconnected would
    /// cancel the probe that every other caller in the burst is awaiting, so
    /// healthy requests would see an exception rather than an answer; and since a
    /// cancelled probe never refreshes the window, repeating that would keep the
    /// cache permanently empty and put a pooled connection back on every
    /// anonymous request, which is precisely the cost this class exists to
    /// remove. The probe is bounded by Npgsql's command timeout instead, and a
    /// probe slow enough to matter is a readiness failure the balancer should
    /// see as one. The probe delegate takes no token either, so this cannot be
    /// reintroduced by a caller who assumes one would be honoured.
    /// </remarks>
    public Task<bool> IsReadyAsync()
    {
        lock (_sync)
        {
            if (clock() < _expiresAt)
            {
                return Task.FromResult(_ready);
            }

            // Hand a burst the single in-flight probe rather than starting one
            // each. Sharing the Task rather than gating on a semaphore is what
            // keeps this type free of disposable state, and it means callers
            // that arrive mid-probe wait for that probe instead of queueing
            // behind it to run their own.
            //
            // Only an UNCOMPLETED task is shared, and _inFlight is never cleared
            // from inside the probe. Clearing it there looks equivalent and is
            // not: a probe that completes synchronously runs its cleanup before
            // the assignment below stores the task, so the field would be left
            // holding a completed task that nothing ever cleared and the cache
            // would answer from it forever. Replacing a completed task here
            // instead keeps every mutation under this lock, which also means a
            // faulted probe is simply retried by the next caller.
            if (_inFlight is { IsCompleted: false })
            {
                return _inFlight;
            }

            return _inFlight = ProbeAsync();
        }
    }

    private async Task<bool> ProbeAsync()
    {
        // Yield before touching the probe so no injected code — a DI resolve and
        // Npgsql's synchronous pre-await path, in this app's case — ever runs
        // while _sync is held: ProbeAsync is started from inside that lock, and
        // without this the lock would cover arbitrary caller-supplied work
        // rather than three fields. It also means the continuation below always
        // takes _sync fresh on a pool thread rather than re-entering it on the
        // starting thread, so correctness stops depending on Lock being
        // recursive.
        await Task.Yield();

        bool ready = await probe().ConfigureAwait(false);
        lock (_sync)
        {
            _ready = ready;
            _expiresAt = clock() + window;
        }

        // A throwing probe deliberately does not reach here, so it refreshes
        // nothing: an exception is a bug in the probe, not an answer about the
        // database, and caching it would hide it for the window's duration.
        return ready;
    }
}
