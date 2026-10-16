using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.WhatsApp;

/// <summary>WhatsApp message templates. Returned entities are tracked unless "ReadOnly".</summary>
public interface IWhatsAppTemplateRepository
{
    void Add(WhatsAppTemplate template);

    Task<WhatsAppTemplate?> GetAsync(Guid clinicId, Guid templateId, CancellationToken ct = default);

    /// <summary>Read-only copies, newest first (cached).</summary>
    Task<IReadOnlyList<WhatsAppTemplate>> ListForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only copy, from the cached list.</summary>
    Task<WhatsAppTemplate?> GetReadOnlyAsync(Guid clinicId, Guid templateId, CancellationToken ct = default);

    Task<WhatsAppTemplate?> FindByProviderTemplateIdAsync(Guid clinicId, string providerTemplateId, CancellationToken ct = default);

    Task<WhatsAppTemplate?> FindByNameAsync(Guid clinicId, string name, string language, CancellationToken ct = default);
}
