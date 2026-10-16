using System.Globalization;
using Microsoft.Extensions.Options;
using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

public interface IEntitlementService
{
    Task<ClinicEntitlements> GetAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Throws EntitlementDeniedException when the clinic has no active plan.</summary>
    Task EnsureCanSendMessagesAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Throws EntitlementDeniedException when the feature isn't in the clinic's active plan.</summary>
    Task EnsureFeatureAsync(Guid clinicId, string featureKey, CancellationToken ct = default);

    /// <summary>Throws EntitlementDeniedException when <paramref name="countAfterChange"/> would exceed the limit.</summary>
    Task EnsureWithinLimitAsync(Guid clinicId, string limitKey, int countAfterChange, CancellationToken ct = default);

    /// <summary>Before connecting (or reconnecting) a messaging channel: checks max_channel_connections, and
    /// max_whatsapp_numbers for WhatsApp. Reconnecting the same channel doesn't count twice.</summary>
    Task EnsureCanConnectChannelAsync(Guid clinicId, string channel, CancellationToken ct = default);
}
