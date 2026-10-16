namespace Omni.App.Entities.Dtos.Monitoring;

/// <summary>What a trace step is about: any of client, conversation, message, channel and the provider's message id.</summary>
public readonly record struct TraceIds(
    Guid? ClientId = null,
    Guid? ConversationId = null,
    Guid? MessageId = null,
    string? Channel = null,
    string? ExternalMessageId = null);