using Omni.App.Business.Contracts.Services.Calendars;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Every couple of minutes, fails calendar syncs that got no answer within Calendars / SyncAnswerTimeoutMinutes and marks their
/// connections unhealthy (ICalendarIntegrationService.FailUnansweredSyncsAsync), so a broken sync engine shows up within minutes
/// instead of the client believing their calendar is in sync (QA t9-03).
/// </summary>
public sealed class CalendarSyncWatchdogJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CalendarSyncWatchdogJob> _logger;

    public CalendarSyncWatchdogJob(IServiceScopeFactory scopes, ILogger<CalendarSyncWatchdogJob> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // let the app finish starting
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>One check. Returns how many syncs it failed (0 on an error, which is logged and retried next time).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var failed = await scope.ServiceProvider.GetRequiredService<ICalendarIntegrationService>().FailUnansweredSyncsAsync(ct);
            if (failed > 0) _logger.LogWarning("{Count} calendar sync(s) got no answer in time and were marked failed.", failed);
            return failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Checking for unanswered calendar syncs failed; retrying next interval.");
            return 0;
        }
    }
}
