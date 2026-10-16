using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.Channels;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>
/// Messaging channels, calendar and TikTok connections across clients, for the admin portal. Writes go through the same
/// services a client uses (disconnect at the provider, cache clearing, plan limits), so an admin change behaves exactly
/// like the client's own.
/// </summary>
public interface IChannelAdminService
{
    Task<List<ChannelRow>> ListChannelsAsync(Guid? clientId, string? channel, bool problemsOnly, CancellationToken ct);

    Task<ChannelDetail?> GetChannelAsync(Guid id, CancellationToken ct);

    Task<List<HealthEventRow>> HealthEventsAsync(Guid channelIntegrationId, CancellationToken ct);

    Task<List<CalendarRow>> ListCalendarsAsync(Guid? clientId, CancellationToken ct);

    Task<List<TikTokRow>> ListTikTokAsync(Guid? clientId, CancellationToken ct);

    Task<PlatformAdminChange> DisconnectAsync(Guid id, CancellationToken ct);

    Task<PlatformAdminChange> DisconnectCalendarAsync(Guid id, CancellationToken ct);

    Task<PlatformAdminChange> SetCalendarSyncAsync(Guid id, bool enabled, CancellationToken ct);

    Task<PlatformAdminChange> DisconnectTikTokAsync(Guid id, CancellationToken ct);

    /// <summary>Inspects an access token (or exchanges an embedded-signup code first) with Omni's own Meta app.</summary>
    Task<WhatsAppTokenDebugResult> DebugWhatsAppTokenAsync(DebugTokenRequest request, CancellationToken ct);
}
