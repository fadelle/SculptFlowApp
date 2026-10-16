using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Inbox;

/// <summary>Inbox settings rows (crm.inbox_settings). Only the clinic-wide row (no channel connection) is used today.</summary>
public interface IInboxSettingsRepository
{
    /// <summary>The clinic-wide row, tracked so changes are written on the next save; null when the clinic has none.</summary>
    Task<InboxSettings?> GetForClinicAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>The clinic-wide row as a read-only copy (cached, "none" too); null when the clinic has none.</summary>
    Task<InboxSettings?> GetForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    void Add(InboxSettings settings);
}
