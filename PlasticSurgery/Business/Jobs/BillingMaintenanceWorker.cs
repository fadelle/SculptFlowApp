using Microsoft.Extensions.Options;
using PlasticSurgery.Business.Contracts.Engines.Billing;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Common.Configs;

namespace PlasticSurgery.Business.Jobs;

/// <summary>
/// The one billing background job (in-process, every Billing:MaintenanceIntervalMinutes):
///   1. subscriptions whose period ended: renew / past_due / expire / end a scheduled cancellation
///   2. reservations older than Billing:ReservationTimeoutHours: settle if the message was delivered after all,
///      otherwise release
/// Each clinic is its own transaction under its billing-account lock, and every step is idempotent, so a crash,
/// a restart or two app instances running it at once can't charge twice. Idle while Billing:Enabled is false.
/// </summary>
public class BillingMaintenanceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly BillingOptions _options;
    private readonly ILogger<BillingMaintenanceWorker> _logger;

    public BillingMaintenanceWorker(IServiceScopeFactory scopes, IOptions<BillingOptions> options, ILogger<BillingMaintenanceWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Billing is disabled (Billing:Enabled = false); the billing maintenance worker is idle.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(_options.MaintenanceIntervalMinutes, 1, 60));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // let the app finish starting
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var changed = await scope.ServiceProvider.GetRequiredService<ISubscriptionService>().ProcessDueAsync(ct);
            var resolved = await scope.ServiceProvider.GetRequiredService<IMessageBillingService>().ResolveStaleReservationsAsync(ct);
            if (changed > 0 || resolved > 0)
            {
                _logger.LogInformation("Billing maintenance: {Subscriptions} subscription change(s), {Reservations} stale reservation(s) resolved.", changed, resolved);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing is half-done (every step is its own transaction); the next run picks up where this one stopped.
            _logger.LogError(ex, "Billing maintenance run failed; retrying next interval.");
        }
    }
}
