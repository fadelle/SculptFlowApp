using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Bookings;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Sends booking reminders (in-process, every Reminders:IntervalSeconds): asks IBookingReminderService for the clients with
/// reminders on and handles each client's due reminders in its own scope (its own DbContext), so one client's failure
/// can't poison another's. A reminder is claimed with a unique row before it is sent, so a second app instance never
/// sends it twice; a failed send is recorded, not retried.
/// </summary>
public class BookingReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BookingReminderWorker> _logger;
    private readonly IConfigManager _config;

    public BookingReminderWorker(IServiceScopeFactory scopes, ILogger<BookingReminderWorker> logger, IConfigManager config)
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
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // let the app finish starting
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_config.RemindersIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>One pass over every client with reminders on. Returns how many reminder rows it wrote.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> clients;
        try
        {
            using var scope = _scopes.CreateScope();
            clients = await scope.ServiceProvider.GetRequiredService<IBookingReminderService>().ListClientsWithRemindersAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reminder worker couldn't list clients; retrying next interval.");
            return 0;
        }

        var written = 0;
        foreach (var clientId in clients)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = _scopes.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<IBookingReminderService>().SendDueAsync(clientId, ct);
                if (count > 0) _logger.LogInformation("Booking reminders: {Count} handled for client {ClientId}.", count, clientId);
                written += count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Reminder worker failed for client {ClientId}; retrying next interval.", clientId);
            }
        }
        return written;
    }
}
