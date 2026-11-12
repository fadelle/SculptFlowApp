namespace Omni.App.Entities.Dtos.Contacts;

/// <summary>The contact fields a CSV column can fill (QA t12-fix-05).</summary>
public static class ContactImportField
{
    public const string Name = "name";
    public const string Phone = "phone";
    public const string Email = "email";
    public const string City = "city";
    public const string Notes = "notes";
    public const string Stage = "stage";

    public static readonly IReadOnlyList<string> All = [Name, Phone, Email, City, Notes, Stage];
}

/// <summary>What the uploaded file holds: its header, the first rows, how many rows, and the columns guessed for each field.</summary>
public sealed record ContactImportPreview(string[] Headers, IReadOnlyList<string[]> Sample, int RowCount, IReadOnlyDictionary<string, int> Mapping);

/// <summary>One row that didn't go in: its line in the file (the header is line 1) and why, in the member's language.</summary>
public sealed record ContactImportFailure(int Line, string Reason);

/// <summary>What an import did.</summary>
public sealed record ContactImportResult(int Added, int Updated, int Skipped, IReadOnlyList<ContactImportFailure> Failed);
