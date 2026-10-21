using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Dtos.Contacts;

namespace Omni.App.Business.Contracts.Services.Contacts;

/// <summary>
/// Privacy requests for one contact: export everything Omni holds about them (one JSON document) and delete it for good.
/// In the dashboard only the account owner (role <c>client</c>) may do either (UnauthorizedAccessException, 403); the
/// admin portal may do both for any client. A delete is confirmed by typing the contact's name (else phone, else email)
/// and logs a <c>contact_deleted</c> event without any link to the contact.
/// </summary>
public interface IContactDataService
{
    /// <summary>Null when the contact isn't the actor's client's.</summary>
    Task<ContactExport?> ExportAsync(UserAccess actor, Guid contactId, CancellationToken ct = default);

    /// <summary>False when the contact isn't the actor's client's. ArgumentException (contactDetail.data.errors.confirmMismatch)
    /// when the confirmation doesn't match.</summary>
    Task<bool> DeleteAsync(UserAccess actor, Guid contactId, string? confirmation, CancellationToken ct = default);

    /// <summary>The admin portal's export of any client's contact; null when unknown.</summary>
    Task<ContactExport?> ExportAnyAsync(Guid contactId, CancellationToken ct = default);

    /// <summary>The admin portal's delete of any client's contact; returns its client, or null when unknown.</summary>
    Task<Guid?> DeleteAnyAsync(Guid contactId, CancellationToken ct = default);
}
