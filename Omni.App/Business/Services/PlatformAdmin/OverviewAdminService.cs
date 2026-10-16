using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

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

    public Task<List<ClientRow>> RecentClientsAsync(int take, CancellationToken ct) => _repository.RecentClientsAsync(Math.Clamp(take, 1, 100), ct);

    public Task<PagedResponse<EventRow>> EventsAsync(Guid? clientId, string? eventType, int page, CancellationToken ct) =>
        _repository.EventsAsync(clientId, eventType, page, ct);

    public Task<List<string>> EventTypesAsync(CancellationToken ct) => _repository.EventTypesAsync(ct);
}
