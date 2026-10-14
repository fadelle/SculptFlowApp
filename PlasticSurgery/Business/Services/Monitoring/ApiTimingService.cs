using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Monitoring;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Monitoring;

namespace PlasticSurgery.Business.Services.Monitoring;

public class ApiTimingService : IApiTimingService
{
    private readonly IApiRouteStatRepository _stats;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;

    public ApiTimingService(IApiRouteStatRepository stats, IUnitOfWork unitOfWork, IConfigManager config, TimeProvider time)
    {
        _stats = stats;
        _unitOfWork = unitOfWork;
        _config = config;
        _time = time;
    }

    public async Task SaveAsync(IReadOnlyCollection<ApiRouteStat> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var now = _time.GetUtcNow();
        foreach (var row in rows) row.CreatedAt = now;
        _stats.AddRange(rows);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task<int> PurgeAsync(CancellationToken ct) =>
        _stats.DeleteOlderThanAsync(_time.GetUtcNow().AddDays(-_config.MonitoringRetentionDays), ct);
}
