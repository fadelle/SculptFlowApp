using PlasticSurgery.Entities.Dtos.PlatformAdmin;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>
/// Messaging channels, calendar and TikTok connections across clinics, for the admin portal. Writes go through the same
/// services a clinic uses (disconnect at the provider, cache clearing, plan limits), so an admin change behaves exactly
/// like the clinic's own.
/// </summary>
public interface IChannelAdminService
{
    Task<List<ChannelRow>> ListChannelsAsync(Guid? clinicId, string? channel, bool problemsOnly, CancellationToken ct);

    Task<ChannelDetail?> GetChannelAsync(Guid id, CancellationToken ct);

    Task<List<HealthEventRow>> HealthEventsAsync(Guid channelIntegrationId, CancellationToken ct);

    Task<List<CalendarRow>> ListCalendarsAsync(Guid? clinicId, CancellationToken ct);

    Task<List<TikTokRow>> ListTikTokAsync(Guid? clinicId, CancellationToken ct);

    Task<PlatformAdminChange> DisconnectAsync(Guid id, CancellationToken ct);

    /// <summary>Connects (or moves) the clinic's WhatsApp to an Infobip sender, checked live against Infobip.</summary>
    Task<ChannelDetail> ConnectInfobipSenderAsync(Guid clinicId, string? senderNumber, CancellationToken ct);

    Task<PlatformAdminChange> DisconnectCalendarAsync(Guid id, CancellationToken ct);

    Task<PlatformAdminChange> SetCalendarSyncAsync(Guid id, bool enabled, CancellationToken ct);

    Task<PlatformAdminChange> DisconnectTikTokAsync(Guid id, CancellationToken ct);
}
