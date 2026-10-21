using System.Text.Encodings.Web;
using System.Text.Json;
using Omni.App.Entities.Dtos.Contacts;

namespace Omni.App.Common.Helpers;

/// <summary>A contact export as a downloadable file: indented camelCase JSON (non-Latin text kept readable), named by date
/// and the contact id, never by the person's name.</summary>
public static class ContactExportFile
{
    public const string ContentType = "application/json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static byte[] Bytes(ContactExport export) => JsonSerializer.SerializeToUtf8Bytes(export, Json);

    public static string Name(ContactExport export) =>
        $"contact-{export.ExportedAt:yyyy-MM-dd}-{export.Contact.Id.ToString("N")[..8]}.json";
}
