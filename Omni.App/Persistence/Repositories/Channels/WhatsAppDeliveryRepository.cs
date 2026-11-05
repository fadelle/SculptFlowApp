using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Persistence.Repositories.Channels;

public class WhatsAppDeliveryRepository : IWhatsAppDeliveryRepository
{
    private readonly ApplicationDbContext _db;

    public WhatsAppDeliveryRepository(ApplicationDbContext db) => _db = db;

    public async Task<bool> EnqueueAsync(string body, string bodyHash, string? traceParent, DateTimeOffset now, CancellationToken ct = default) =>
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into channels.whatsapp_webhook_deliveries (body, body_hash, trace_parent, status, attempts, received_at, next_attempt_at)
            values ({body}, {bodyHash}, {traceParent}, 'pending', 0, {now}, {now})
            on conflict (body_hash) do nothing
            """, ct) == 1;

    public async Task<WhatsAppWebhookDelivery?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset lockedUntil, CancellationToken ct = default) =>
        (await _db.WhatsAppWebhookDeliveries.FromSqlInterpolated($"""
            update channels.whatsapp_webhook_deliveries d
               set status = 'processing', attempts = d.attempts + 1, locked_until = {lockedUntil}
             where d.id = (select id from channels.whatsapp_webhook_deliveries
                            where (status = 'pending' and next_attempt_at <= {now})
                               or (status = 'processing' and locked_until < {now})
                            order by id
                            for update skip locked
                            limit 1)
            returning d.*
            """).AsNoTracking().ToListAsync(ct)).FirstOrDefault();

    public Task CompleteAsync(long id, DateTimeOffset now, CancellationToken ct = default) =>
        _db.WhatsAppWebhookDeliveries.Where(d => d.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Status, WhatsAppDeliveryStatus.Done)
            .SetProperty(d => d.ProcessedAt, now)
            .SetProperty(d => d.LockedUntil, (DateTimeOffset?)null)
            .SetProperty(d => d.LastError, (string?)null), ct);

    public Task RetryLaterAsync(long id, string error, DateTimeOffset retryAt, CancellationToken ct = default) =>
        _db.WhatsAppWebhookDeliveries.Where(d => d.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Status, WhatsAppDeliveryStatus.Pending)
            .SetProperty(d => d.NextAttemptAt, retryAt)
            .SetProperty(d => d.LockedUntil, (DateTimeOffset?)null)
            .SetProperty(d => d.LastError, error), ct);

    public Task FailAsync(long id, string error, DateTimeOffset now, CancellationToken ct = default) =>
        _db.WhatsAppWebhookDeliveries.Where(d => d.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Status, WhatsAppDeliveryStatus.Failed)
            .SetProperty(d => d.ProcessedAt, now)
            .SetProperty(d => d.LockedUntil, (DateTimeOffset?)null)
            .SetProperty(d => d.LastError, error), ct);

    public Task<int> DeleteDoneBeforeAsync(DateTimeOffset before, CancellationToken ct = default) =>
        _db.WhatsAppWebhookDeliveries.Where(d => d.Status == WhatsAppDeliveryStatus.Done && d.ProcessedAt < before).ExecuteDeleteAsync(ct);
}
