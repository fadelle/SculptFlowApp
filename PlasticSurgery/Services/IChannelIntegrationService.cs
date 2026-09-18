using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IChannelIntegrationService
{
    /// <summary>
    /// Returns one row per known channel (Data.Entities.ChannelType.All) for the clinic — a
    /// synthetic "disconnected" row (not persisted) for any channel that has never been saved,
    /// so the Settings page always has a card for every channel.
    /// </summary>
    Task<IReadOnlyList<ChannelIntegrationResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);

    Task<ChannelIntegrationResponse> SaveAsync(SaveChannelIntegrationRequest request, CancellationToken ct = default);

    Task DisconnectAsync(Guid clinicId, string channel, CancellationToken ct = default);

    /// <summary>Completes WhatsApp Embedded Signup: exchanges the code for a token, looks up the phone number, and saves it.</summary>
    Task<ChannelIntegrationResponse> ConnectWhatsAppAsync(ConnectWhatsAppRequest request, CancellationToken ct = default);

    /// <summary>Completes Facebook Login: exchanges the code for a long-lived token, picks the user's first managed Page, and saves it.</summary>
    Task<ChannelIntegrationResponse> ConnectFacebookAsync(ConnectFacebookRequest request, CancellationToken ct = default);
}
