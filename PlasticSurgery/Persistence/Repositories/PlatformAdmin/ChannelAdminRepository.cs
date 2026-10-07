using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Persistence.Repositories.PlatformAdmin;

public class ChannelAdminRepository : IChannelAdminRepository
{
    private readonly ApplicationDbContext _db;

    public ChannelAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<List<ChannelRow>> ListChannelsAsync(Guid? clinicId, string? channel, bool problemsOnly, CancellationToken ct = default)
    {
        var q = _db.ChannelIntegrations.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(c => c.ClinicId == clinicId.Value);
        if (!string.IsNullOrWhiteSpace(channel)) q = q.Where(c => c.Channel == channel);
        if (problemsOnly) q = q.Where(c => c.Status == ChannelIntegrationStatus.Error || !c.IsHealthy || c.LastError != null);
        return q.OrderBy(c => c.Clinic!.Name).ThenBy(c => c.Channel)
            .Select(c => new ChannelRow(c.Id, c.ClinicId, c.Clinic!.Name, c.Channel,
                c.Provider ?? (c.Channel == ChannelType.Telegram ? "telegram" : ChannelProvider.Meta),
                c.Status, c.DisplayName,
                c.ProviderSenderId ?? c.PhoneNumberId ?? c.TelegramBotUsername ?? c.PageId,
                c.HealthLevel, c.IsHealthy, c.LastProblemMessage, c.LastError, c.WebhookStatus, c.LastWebhookAt,
                c.LastVerifiedAt, c.UpdatedAt))
            .ToListAsync(ct);
    }

    public Task<ChannelIntegration?> GetChannelAsync(Guid id, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AsNoTracking().Include(c => c.Clinic).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<WhatsAppHealthEvent>> HealthEventsAsync(Guid channelIntegrationId, CancellationToken ct = default) =>
        _db.WhatsAppHealthEvents.AsNoTracking().Where(e => e.ChannelIntegrationId == channelIntegrationId)
            .OrderByDescending(e => e.OccurredAt).Take(100).ToListAsync(ct);

    public Task<List<CalendarRow>> ListCalendarsAsync(Guid? clinicId, CancellationToken ct = default)
    {
        var q = _db.CalendarIntegrations.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(c => c.ClinicId == clinicId.Value);
        return q.OrderByDescending(c => c.UpdatedAt)
            .Select(c => new CalendarRow(c.Id, c.ClinicId,
                _db.Clinics.Where(x => x.Id == c.ClinicId).Select(x => x.Name).FirstOrDefault() ?? "",
                c.Provider, c.Status, c.AccountDisplayName, c.SelectedCalendarName, c.SyncEnabled, c.IsHealthy,
                c.LastProblemMessage, c.LastSyncedAt,
                _db.AppointmentCalendarSyncs.Count(s => s.CalendarIntegrationId == c.Id && s.Status == AppointmentCalendarSyncStatus.Failed)))
            .ToListAsync(ct);
    }

    public Task<List<TikTokRow>> ListTikTokAsync(Guid? clinicId, CancellationToken ct = default)
    {
        var q = _db.TikTokIntegrations.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(c => c.ClinicId == clinicId.Value);
        return q.OrderByDescending(c => c.UpdatedAt)
            .Select(c => new TikTokRow(c.Id, c.ClinicId,
                _db.Clinics.Where(x => x.Id == c.ClinicId).Select(x => x.Name).FirstOrDefault() ?? "",
                c.Status, c.DisplayName, c.IsHealthy, c.LastProblemMessage, c.TokenExpiresAt, c.UpdatedAt))
            .ToListAsync(ct);
    }

    public Task<CalendarIntegration?> GetCalendarAsync(Guid id, CancellationToken ct = default) =>
        _db.CalendarIntegrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<TikTokIntegration?> GetTikTokAsync(Guid id, CancellationToken ct = default) =>
        _db.TikTokIntegrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
}
