using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.WhatsApp;

/// <summary>WhatsApp message templates. Returned entities are tracked unless "ReadOnly".</summary>
public interface IWhatsAppTemplateRepository
{
    void Add(WhatsAppTemplate template);

    Task<WhatsAppTemplate?> GetAsync(Guid clientId, Guid templateId, CancellationToken ct = default);

    /// <summary>Read-only copies, newest first (cached).</summary>
    Task<IReadOnlyList<WhatsAppTemplate>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Read-only copy, from the cached list.</summary>
    Task<WhatsAppTemplate?> GetReadOnlyAsync(Guid clientId, Guid templateId, CancellationToken ct = default);

    Task<WhatsAppTemplate?> FindByProviderTemplateIdAsync(Guid clientId, string providerTemplateId, CancellationToken ct = default);

    Task<WhatsAppTemplate?> FindByNameAsync(Guid clientId, string name, string language, CancellationToken ct = default);
}
