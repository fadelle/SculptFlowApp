using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.WhatsApp;

namespace Omni.App.Persistence.Repositories.WhatsApp;

public class WhatsAppHealthEventRepository : IWhatsAppHealthEventRepository
{
    private readonly ApplicationDbContext _db;

    public WhatsAppHealthEventRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(WhatsAppHealthEvent healthEvent) => _db.WhatsAppHealthEvents.Add(healthEvent);

    public async Task<IReadOnlyList<WhatsAppHealthEvent>> ListForClientAsync(Guid clientId, int skip, int take, CancellationToken ct = default) =>
        await _db.WhatsAppHealthEvents
            .Where(h => h.ClientId == clientId)
            .OrderByDescending(h => h.OccurredAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid channelIntegrationId, string eventType, DateTimeOffset occurredAt, CancellationToken ct = default) =>
        _db.WhatsAppHealthEvents.AnyAsync(h =>
            h.ChannelIntegrationId == channelIntegrationId && h.EventType == eventType && h.OccurredAt == occurredAt, ct);
}
