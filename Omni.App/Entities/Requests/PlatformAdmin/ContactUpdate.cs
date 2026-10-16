namespace Omni.App.Entities.Requests.PlatformAdmin;

/// <summary>StageKey must be a stage key of the contact's client's business type.</summary>
public record ContactUpdate(string StageKey, string Qualification, bool MarketingOptIn, string? Notes);
