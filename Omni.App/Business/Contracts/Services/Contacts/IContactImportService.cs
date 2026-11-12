using Omni.App.Entities.Dtos.Contacts;

namespace Omni.App.Business.Contracts.Services.Contacts;

/// <summary>The contact list from and to CSV (QA t12-fix-05). Errors are strings-file keys: ArgumentException 400 for a file
/// that is empty, too big (1 MB) or too long (2,000 rows), or a column choice that names no name or phone column.</summary>
public interface IContactImportService
{
    /// <summary>The file's header, first rows and the columns guessed for each field, before anything is saved.</summary>
    ContactImportPreview Preview(string csv);

    /// <summary>Adds each row (as "Add contact" would) or, for a phone the client already has, skips it or updates the contact
    /// with the row's filled cells. A bad row is reported with its line and the rest still go in.</summary>
    Task<ContactImportResult> ImportAsync(Guid clientId, string csv, IReadOnlyDictionary<string, int> mapping, bool updateExisting,
        CancellationToken ct = default);

    /// <summary>The contacts matching the Contacts list's filters as CSV (UTF-8 with a byte-order mark, up to 20,000 rows).</summary>
    Task<string> ExportAsync(Guid clientId, string? stage, string? search, string? source, CancellationToken ct = default);
}
