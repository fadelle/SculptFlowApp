using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Statics;

namespace PlasticSurgery.Business.Jobs;

/// <summary>Loads config.settings into the configuration manager at startup, then refreshes it every minute.</summary>
public sealed class ConfigRefreshJob : BackgroundService
{
    private readonly IConfigManager _config;

    public ConfigRefreshJob(IConfigManager config)
    {
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ConfigSettingLimits.RefreshSeconds));
        do
        {
            await _config.RefreshAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
