using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

public interface IOverviewAdminRepository
{
    Task<SystemTotals> TotalsAsync(CancellationToken ct = default);

    /// <summary>Messages per UTC day for the last <paramref name="days"/> days, split by who sent them.</summary>
    Task<List<DailyCount>> DailyMessagesAsync(int days = 14, CancellationToken ct = default);

    /// <summary>Everything that probably needs an admin: broken channels and calendars, failing sends, stuck campaigns.</summary>
    Task<List<ProblemRow>> ProblemsAsync(CancellationToken ct = default);

    Task<List<ClientRow>> RecentClientsAsync(int take, CancellationToken ct = default);

    Task<PagedResponse<EventRow>> EventsAsync(Guid? clientId, string? eventType, int page, CancellationToken ct = default);

    Task<List<string>> EventTypesAsync(CancellationToken ct = default);

}
