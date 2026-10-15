using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Monitoring;

namespace PlasticSurgery.Persistence.Repositories.Monitoring;

public class MessageTraceStepRepository : IMessageTraceStepRepository
{
    private readonly ApplicationDbContext _db;

    public MessageTraceStepRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void AddRange(IEnumerable<MessageTraceStep> rows) => _db.MessageTraceSteps.AddRange(rows);

    public Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, DateTimeOffset failedCutoff, CancellationToken ct = default)
    {
        // Steps past the normal retention go, unless their trace failed somewhere and is still inside the failed retention.
        // (A failed retention shorter than the normal one simply never keeps anything extra.)
        return _db.MessageTraceSteps
            .Where(s => s.OccurredAt < cutoff
                        && (s.OccurredAt < failedCutoff
                            || !_db.MessageTraceSteps.Any(f => f.TraceId == s.TraceId && f.Outcome == TraceOutcomes.Failed)))
            .ExecuteDeleteAsync(ct);
    }
}