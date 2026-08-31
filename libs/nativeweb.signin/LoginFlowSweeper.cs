using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NativeWeb.SignIn;

/// <summary>
/// Calls <see cref="ILoginFlowStore.SweepAsync"/> on a timer so no consumer behaviour
/// depends on remembering to.
/// </summary>
/// <remarks>
/// Sweeping is GARBAGE COLLECTION ONLY and no security property may be moved into it.
/// Expiry is enforced inside <see cref="ILoginFlowStore.TakeAsync"/>; if it lived here
/// instead, unswept rows would stay live indefinitely and <see cref="FlowTake.Expired"/>
/// would never be returned. What this buys is bounded table growth on a table an
/// unauthenticated endpoint writes to.
/// <para>
/// Every exception is caught and logged. A store that cannot sweep is a table that grows,
/// not a host that stops.
/// </para>
/// <para>
/// The interval and the <see cref="TimeProvider"/> are constructor parameters rather than
/// constants so the sweep loop can be driven in a test without a five-minute wait. The
/// sweep BODY is <see cref="SweepOnceAsync"/> and is tested directly: a test that asserts
/// through the timer asserts the timer, and the behaviour worth pinning here — that a
/// throwing store is caught rather than propagated — has nothing to do with when it runs.
/// </para>
/// </remarks>
internal sealed partial class LoginFlowSweeper : BackgroundService
{
    /// <summary>How often to sweep when the caller does not say.</summary>
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LoginFlowSweeper> _logger;
    private readonly TimeSpan _interval;
    private readonly string _scheme;
    private readonly TimeProvider _clock;

    public LoginFlowSweeper(
        IServiceScopeFactory scopes,
        ILogger<LoginFlowSweeper> logger,
        TimeProvider clock,
        string scheme)
        : this(scopes, logger, clock, scheme, DefaultInterval)
    {
    }

    internal LoginFlowSweeper(
        IServiceScopeFactory scopes,
        ILogger<LoginFlowSweeper> logger,
        TimeProvider clock,
        string scheme,
        TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval), interval, "The sweep interval must be positive.");
        }

        _scopes = scopes;
        _logger = logger;
        _clock = clock;
        _scheme = scheme;
        _interval = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, _clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The wait itself was cancelled: ordinary shutdown, not a fault.
        }
    }

    /// <summary>
    /// One sweep, with its own scope. Returns normally whatever the store does, short of
    /// the host shutting down.
    /// </summary>
    /// <param name="stoppingToken">The host's stopping token.</param>
    internal async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            ILoginFlowStore store = scope.ServiceProvider
                .GetRequiredKeyedService<ILoginFlowStore>(_scheme);
            await store.SweepAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a fault.
        }
#pragma warning disable CA1031 // A sweep is best effort; nothing it throws is worth stopping the host for.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogSweepFailed(_logger, exception);
        }
    }

    [LoggerMessage(1, LogLevel.Warning, "Sweeping expired login flows failed; the next tick will retry.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
