using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>What the AI assistant needs to know about the client it answers for, sent with every AI trigger: the client's
/// name, business type (core.business_types code), default language and timezone, the type's AI profile (the
/// business-specific part of the system prompt) and the type's words in the client's default language
/// (contact, member, service, booking, order, client → singular/plural).</summary>
public record AiClientProfile(
    string ClientName,
    string BusinessType,
    string Language,
    string Timezone,
    string AiProfile,
    IReadOnlyDictionary<string, TermForms> Terms);
