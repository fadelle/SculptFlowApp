using System.Text.Json;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Mappers.PlatformAdmin;

/// <summary>activity.app_logs rows → platform-admin shapes.</summary>
public static class AppLogMapper
{
    public static AppLogDetail ToDetail(AppLog row) => new(
        row.Id, row.OccurredAt, row.Level, row.Category, row.Message, row.MessageTemplate, row.ExceptionType,
        row.ExceptionMessage, row.StackTrace, row.Fingerprint, row.RepeatCount, row.TraceId, row.ClinicId,
        row.ConversationId, row.MessageId, row.Method, row.Route, row.Instance, Properties(row.Properties));

    /// <summary>The stored properties object as name → text (values are always stored as strings); empty if unreadable.</summary>
    public static Dictionary<string, string?> Properties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string?>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, string?>();
            return document.RootElement.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => p.Value.GetString(),
                    _ => p.Value.GetRawText()
                });
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>();
        }
    }
}
