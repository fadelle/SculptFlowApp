namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record ClientRow(Guid Id, string Name, string Slug, string? Email, string Timezone, bool IsActive,
    int MemberCount, int ContactCount, int ConversationCount, DateTimeOffset? LastMessageAt, string WhatsAppStatus,
    DateTimeOffset CreatedAt)
{
    /// <summary>Each connected (or broken) channel with its status (QA t6-admin-24). <see cref="WhatsAppStatus"/> stays for older portals.</summary>
    public IReadOnlyList<ClientChannelState> Channels { get; init; } = [];
}

public record ClientChannelState(string Channel, string Status);
