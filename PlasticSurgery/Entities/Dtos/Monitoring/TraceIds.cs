namespace PlasticSurgery.Entities.Dtos.Monitoring;

/// <summary>What a trace step is about: any of clinic, conversation, message, channel and the provider's message id.</summary>
public readonly record struct TraceIds(
    Guid? ClinicId = null,
    Guid? ConversationId = null,
    Guid? MessageId = null,
    string? Channel = null,
    string? ExternalMessageId = null);