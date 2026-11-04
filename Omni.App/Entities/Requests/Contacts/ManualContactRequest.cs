namespace Omni.App.Entities.Requests.Contacts;

/// <summary>A contact a member adds by hand (POST /api/contacts/manual, Contacts' "Add"): a name or a phone at least.</summary>
public record ManualContactRequest(string? FullName, string? Phone, string? Email, Guid? ServiceId, string? Notes);
