namespace Omni.App.Entities.Requests.Knowledge;

/// <summary>What the upload form supplies. The file itself is passed separately as a stream; the client
/// is never part of this — it comes from CurrentClientContext.</summary>
public record UploadKnowledgeRequest(string? Title, string Category, bool IsActive = true);
