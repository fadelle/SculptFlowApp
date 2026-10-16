using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Monitoring;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Monitoring;

namespace Omni.App.Business.Services.Monitoring;

public class AppLogService : IAppLogService
{
    private static readonly string[] WarningLevels = [AppLogLevels.Warning];
    private static readonly string[] ErrorLevels = [AppLogLevels.Error, AppLogLevels.Critical];

    private readonly IAppLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;

    public AppLogService(IAppLogRepository logs, IUnitOfWork unitOfWork, IConfigManager config, TimeProvider time)
    {
        _logs = logs;
        _unitOfWork = unitOfWork;
        _config = config;
        _time = time;
    }

    public async Task SaveAsync(IReadOnlyCollection<AppLog> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        _logs.AddRange(rows);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var warnings = await _logs.DeleteOlderThanAsync(WarningLevels, now.AddDays(-_config.ErrorLogWarningRetentionDays), ct);
        var errors = await _logs.DeleteOlderThanAsync(ErrorLevels, now.AddDays(-_config.ErrorLogErrorRetentionDays), ct);
        return warnings + errors;
    }
}
