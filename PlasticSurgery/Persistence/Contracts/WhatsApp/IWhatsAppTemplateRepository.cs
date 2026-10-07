using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.WhatsApp;

/// <summary>WhatsApp message templates. Returned entities are tracked.</summary>
public interface IWhatsAppTemplateRepository
{
    void Add(WhatsAppTemplate template);

    Task<WhatsAppTemplate?> GetAsync(Guid clinicId, Guid templateId, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<WhatsAppTemplate>> ListForClinicAsync(Guid clinicId, CancellationToken ct = default);

    Task<WhatsAppTemplate?> FindByProviderTemplateIdAsync(Guid clinicId, string providerTemplateId, CancellationToken ct = default);

    Task<WhatsAppTemplate?> FindByNameAsync(Guid clinicId, string name, string language, CancellationToken ct = default);

    /// <summary>The clinic owning an Infobip template, by the provider's template id; null when unknown.</summary>
    Task<Guid?> FindInfobipTemplateClinicIdAsync(string? providerTemplateId, CancellationToken ct = default);
}
