using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

public interface IChannelAdminRepository
{
    Task<List<ChannelRow>> ListChannelsAsync(Guid? clinicId, string? channel, bool problemsOnly, CancellationToken ct = default);

    Task<ChannelIntegration?> GetChannelAsync(Guid id, CancellationToken ct = default);

    Task<List<WhatsAppHealthEvent>> HealthEventsAsync(Guid channelIntegrationId, CancellationToken ct = default);

    Task<List<CalendarRow>> ListCalendarsAsync(Guid? clinicId, CancellationToken ct = default);

    Task<List<TikTokRow>> ListTikTokAsync(Guid? clinicId, CancellationToken ct = default);

    Task<CalendarIntegration?> GetCalendarAsync(Guid id, CancellationToken ct = default);

    Task<TikTokIntegration?> GetTikTokAsync(Guid id, CancellationToken ct = default);
}
