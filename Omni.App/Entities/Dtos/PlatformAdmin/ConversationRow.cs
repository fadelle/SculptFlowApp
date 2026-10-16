namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record ConversationRow(Guid Id, Guid ClientId, string ClientName, Guid LeadId, string LeadName, string Channel,
    string Status, string Mode, DateTimeOffset? LastMessageAt, string? LastMessageDirection, int MessageCount, DateTimeOffset CreatedAt);
