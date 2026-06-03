// -----------------------------------------------------------------------
// <copyright file="DaemonStatusFeed.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Single-source poller of <see cref="DaemonClientService.GetStatusAsync"/>.
/// All pages and the topbar subscribe via <see cref="Updated"/> so the
/// daemon is hit by exactly one request per poll interval regardless of
/// how many components are mounted. See netclaw-web-ui spec, "Shared
/// daemon status feed".
///
/// This type is a singleton (it owns the polling loop and the shared event)
/// but <see cref="DaemonClientService"/> is scoped — each poll resolves a
/// fresh client inside its own DI scope so the underlying Refit/HttpClient
/// participates in <c>IHttpClientFactory</c>'s handler rotation rather than
/// being pinned for the host's lifetime.
/// </summary>
public sealed class DaemonStatusFeed : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DaemonTargetStore _targets;

    public DaemonStatusFeed(IServiceScopeFactory scopeFactory, DaemonTargetStore targets)
    {
        _scopeFactory = scopeFactory;
        _targets = targets;
    }

    // Live so the topbar reflects an operator re-point on the next poll.
    public string Endpoint => _targets.EffectiveEndpoint;

    public DaemonApiResult<DaemonRuntimeStatus.Response>? Current { get; private set; }

    public event Action<DaemonApiResult<DaemonRuntimeStatus.Response>>? Updated;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Prime once before the loop so first-mount subscribers can see a
        // result without waiting a full interval.
        await PollOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PollOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) { } // slopwatch-ignore: SW003 expected on host shutdown.
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<DaemonClientService>();
            var result = await client.GetStatusAsync(ct);
            Current = result;
            Updated?.Invoke(result);
        }
        catch (OperationCanceledException) { } // slopwatch-ignore: SW003 cancellation is the shutdown signal.
    }
}
