namespace Omni.App.Entities.Requests.PlatformAdmin;

public record LeadUpdate(string Status, string QualificationStatus, bool MarketingOptIn, string? Notes);
