using Omni.App.Business.Contracts.Engines.WhatsApp;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Processes stored WhatsApp webhook deliveries (QA t10-10): one at a time, oldest first, each in its own scope (its own
/// DbContext), woken by IWhatsAppDeliverySignal or every <see cref="Poll"/>. Done deliveries are deleted after
/// <see cref="Retention"/>. Several app instances can run it: a claim skips deliveries another instance holds.
/// </summary>
public class WhatsAppDeliveryWorker : BackgroundService
{
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IWhatsAppDeliverySignal _signal;
    private readonly ILogger<WhatsAppDeliveryWorker> _logger;

    public WhatsAppDeliveryWorker(IServiceScopeFactory scopes, IWhatsAppDeliverySignal signal, ILogger<WhatsAppDeliveryWorker> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = DateTimeOffset.UtcNow;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await DrainAsync(stoppingToken);
                if (DateTimeOffset.UtcNow >= nextCleanup)
                {
                    await CleanupAsync(stoppingToken);
                    nextCleanup = DateTimeOffset.UtcNow + CleanupEvery;
                }
                await _signal.WaitAsync(Poll, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; a claimed delivery is picked up again when its claim lapses
        }
    }

    /// <summary>Processes due deliveries until none is left. Returns how many were processed.</summary>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var processed = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<IWhatsAppWebhookInbox>().ProcessNextAsync(ct)) break;
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The claim itself failed (database unreachable): wait for the next poll.
                _logger.LogError(ex, "WhatsApp delivery worker couldn't claim a delivery; retrying shortly.");
                break;
            }
        }
        return processed;
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var removed = await scope.ServiceProvider.GetRequiredService<IWhatsAppWebhookInbox>().CleanupAsync(Retention, ct);
            if (removed > 0) _logger.LogInformation("Deleted {Count} processed WhatsApp deliveries older than {Days} days.", removed, Retention.TotalDays);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WhatsApp delivery cleanup failed; retrying next hour.");
        }
    }
}
