using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Channels;

/// <summary>A clinic's messaging channel connections (one row per clinic and channel).</summary>
public interface IChannelIntegrationRepository
{
    void Add(ChannelIntegration integration);

    /// <summary>Tracked.</summary>
    Task<ChannelIntegration?> GetAsync(Guid clinicId, string channel, CancellationToken ct = default);

    Task<ChannelIntegration?> GetReadOnlyAsync(Guid clinicId, string channel, CancellationToken ct = default);

    Task<ChannelIntegration?> GetByIdReadOnlyAsync(Guid integrationId, CancellationToken ct = default);

    /// <summary>Tracked, all channels of the clinic.</summary>
    Task<IReadOnlyList<ChannelIntegration>> ListForClinicAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>The id of the clinic's row for the channel, or Guid.Empty.</summary>
    Task<Guid> GetIdAsync(Guid clinicId, string channel, CancellationToken ct = default);

    /// <summary>Tracked WhatsApp row by Meta phone number id.</summary>
    Task<ChannelIntegration?> FindWhatsAppByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default);

    /// <summary>Tracked WhatsApp row by WhatsApp Business Account id.</summary>
    Task<ChannelIntegration?> FindWhatsAppByWabaIdAsync(string wabaId, CancellationToken ct = default);

    /// <summary>Is this Telegram bot connected to a different clinic?</summary>
    Task<bool> IsTelegramBotConnectedElsewhereAsync(string botId, Guid clinicId, CancellationToken ct = default);

    /// <summary>Is this Infobip WhatsApp sender connected to a different clinic?</summary>
    Task<bool> IsInfobipSenderConnectedElsewhereAsync(string sender, Guid clinicId, CancellationToken ct = default);

    /// <summary>Connected channels of the clinic other than <paramref name="exceptChannel"/>.</summary>
    Task<int> CountConnectedExceptAsync(Guid clinicId, string exceptChannel, CancellationToken ct = default);

    /// <summary>Stamps LastWebhookAt directly in the database.</summary>
    Task TouchLastWebhookAsync(Guid integrationId, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Read-only, all channels of the clinic, by channel.</summary>
    Task<IReadOnlyList<ChannelIntegration>> ListForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default);
}
