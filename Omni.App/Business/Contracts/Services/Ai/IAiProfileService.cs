using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Contracts.Services.Ai;

/// <summary>The client facts the n8n AI assistant reads from the AI trigger (see AiTriggerPayload): who the client is, its
/// business type's AI profile and words, its language and timezone. Read from the cached client and business-type tables.</summary>
public interface IAiProfileService
{
    /// <summary>The client's AI profile, or null when the client doesn't exist. A business type that is missing gives an
    /// empty profile and the default words.</summary>
    Task<AiClientProfile?> GetForClientAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>The language the contact writes in (a two-letter code, see ContactLanguage), null when not known yet. Read
    /// fresh on every trigger (contacts aren't cached), so a language the AI set on the last message is there on the next.</summary>
    Task<string?> GetContactLanguageAsync(Guid clientId, Guid contactId, CancellationToken ct = default);
}
