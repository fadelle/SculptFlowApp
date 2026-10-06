namespace PlasticSurgery.Entities.Requests.Knowledge;

/// <summary>What the upload form supplies. The file itself is passed separately as a stream; the clinic
/// is never part of this — it comes from CurrentClinicContext.</summary>
public record UploadKnowledgeRequest(string? Title, string Category, bool IsActive = true);
