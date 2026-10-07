namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record ClinicRow(Guid Id, string Name, string Slug, string? Email, string Timezone, bool IsActive,
    int StaffCount, int LeadCount, int ConversationCount, DateTimeOffset? LastMessageAt, string WhatsAppStatus,
    DateTimeOffset CreatedAt);
