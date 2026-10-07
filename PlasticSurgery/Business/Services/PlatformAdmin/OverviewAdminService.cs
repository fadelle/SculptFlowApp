using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Business.Services.PlatformAdmin;

public class OverviewAdminService : IOverviewAdminService
{
    private readonly IOverviewAdminRepository _repository;

    public OverviewAdminService(IOverviewAdminRepository repository)
    {
        _repository = repository;
    }

    public Task<SystemTotals> TotalsAsync(CancellationToken ct) => _repository.TotalsAsync(ct);

    public Task<List<DailyCount>> DailyMessagesAsync(int days, CancellationToken ct) => _repository.DailyMessagesAsync(Math.Clamp(days, 1, 90), ct);

    public Task<List<ProblemRow>> ProblemsAsync(CancellationToken ct) => _repository.ProblemsAsync(ct);

    public Task<List<ClinicRow>> RecentClinicsAsync(int take, CancellationToken ct) => _repository.RecentClinicsAsync(Math.Clamp(take, 1, 100), ct);

    public Task<PagedResponse<EventRow>> EventsAsync(Guid? clinicId, string? eventType, int page, CancellationToken ct) =>
        _repository.EventsAsync(clinicId, eventType, page, ct);

    public Task<List<string>> EventTypesAsync(CancellationToken ct) => _repository.EventTypesAsync(ct);
}
