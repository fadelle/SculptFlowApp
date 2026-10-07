namespace PlasticSurgery.Entities.Requests.Knowledge;

/// <summary>multipart/form-data body for POST /api/knowledge/upload — ONE file. No clinicId: the clinic
/// comes from the logged-in user.</summary>
public class KnowledgeUploadForm
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
}
