namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One knowledge document with its content.</summary>
public record KnowledgeDocDetail(Guid Id, Guid ClientId, NamedRef? Client, string Title, string Category, string Content,
    string SourceType, string? OriginalFileName, string? MimeType, long? FileSizeBytes, string? SourceUrl, bool IsActive,
    DateTimeOffset UpdatedAt);
