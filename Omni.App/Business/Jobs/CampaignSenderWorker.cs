using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.Campaigns;

namespace Omni.App.Business.Jobs;

/// <summary>
/// The campaign background sender (in-process, every Campaigns:SenderIntervalSeconds):
///   1. starts every Scheduled campaign whose ScheduledAt has passed (that is what makes "Schedule" actually send)
///   2. sends the next batch (Campaigns:SendBatchSize) of every Running campaign, so a campaign keeps going after
///      the dashboard sent its first batch, with nobody having to press "Send to the rest"
/// Each campaign gets its own scope (its own DbContext), so one campaign's failure can't poison another's. Sending
/// itself goes through ICampaignService, which holds a per-campaign lock (no double sends with the Send button or a
/// second app instance) and re-checks Stop/Cancel before every message. Out of credit or plan: the recipients stay
/// queued and the next run tries again, so the campaign continues by itself after a top-up.
/// </summary>
public class CampaignSenderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CampaignSenderWorker> _logger;
    private readonly IConfigManager _config;

    public CampaignSenderWorker(IServiceScopeFactory scopes, ILogger<CampaignSenderWorker> logger, IConfigManager config)
    {
        _scopes = scopes;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The interval is a setting, read every cycle, so changing it applies without a restart.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); // let the app finish starting
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_config.CampaignsSenderIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>One pass over every due campaign. Returns how many messages it tried to send.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<DueCampaign> due;
        try
        {
            using var scope = _scopes.CreateScope();
            due = await scope.ServiceProvider.GetRequiredService<ICampaignService>().ListDueAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Campaign sender couldn't list due campaigns; retrying next interval.");
            return 0;
        }

        var processed = 0;
        foreach (var campaign in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = _scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<ICampaignService>()
                    .SendDueAsync(campaign.ClinicId, campaign.Id, _config.CampaignsSendBatchSize, ct);
                if (result is null) continue;

                processed += result.Processed;
                if (result.Processed > 0 || result.CampaignCompleted)
                {
                    _logger.LogInformation(
                        "Campaign {CampaignId}: sent {Succeeded}, failed {Failed}, {Remaining} still queued{Completed}.",
                        campaign.Id, result.Succeeded, result.Failed, result.RemainingQueued, result.CampaignCompleted ? ", completed" : "");
                }
            }
            catch (Exception ex) when (ex is BillingDeniedException or EntitlementDeniedException)
            {
                // Recipients stay queued; the next run tries again (e.g. after the clinic tops up or changes plan).
                _logger.LogWarning("Campaign {CampaignId} is waiting: {Reason}", campaign.Id, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Campaign sender failed on campaign {CampaignId}; retrying next interval.", campaign.Id);
            }
        }

        return processed;
    }
}
