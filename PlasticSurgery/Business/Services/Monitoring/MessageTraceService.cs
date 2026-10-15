using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Monitoring;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Monitoring;

namespace PlasticSurgery.Business.Services.Monitoring;

public class MessageTraceService : IMessageTraceService
{
    private readonly IMessageTraceStepRepository _steps;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;

    public MessageTraceService(IMessageTraceStepRepository steps, IUnitOfWork unitOfWork, IConfigManager config, TimeProvider time)
    {
        _steps = steps;
        _unitOfWork = unitOfWork;
        _config = config;
        _time = time;
    }

    public async Task SaveAsync(IReadOnlyCollection<MessageTraceStep> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        _steps.AddRange(rows);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task<int> PurgeAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        return _steps.DeleteExpiredAsync(now.AddDays(-_config.MessageTraceRetentionDays), now.AddDays(-_config.MessageTraceFailedRetentionDays), ct);
    }
}