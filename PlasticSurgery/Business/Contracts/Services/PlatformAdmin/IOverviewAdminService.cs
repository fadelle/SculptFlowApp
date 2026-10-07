using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>Platform-wide totals, problems and the event log, for the admin portal's dashboard and Events page.</summary>
public interface IOverviewAdminService
{
    Task<SystemTotals> TotalsAsync(CancellationToken ct);

    Task<List<DailyCount>> DailyMessagesAsync(int days, CancellationToken ct);

    Task<List<ProblemRow>> ProblemsAsync(CancellationToken ct);

    Task<List<ClinicRow>> RecentClinicsAsync(int take, CancellationToken ct);

    Task<PagedResponse<EventRow>> EventsAsync(Guid? clinicId, string? eventType, int page, CancellationToken ct);

    Task<List<string>> EventTypesAsync(CancellationToken ct);
}
