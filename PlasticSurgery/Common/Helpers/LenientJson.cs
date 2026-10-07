using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Common.Helpers;

/// <summary>For optional values the AI's n8n tool fills in: LLMs send "" / "null" / "none" instead of omitting a field they have no
/// value for. Those mean "not provided" (null); a real value must still parse or the request is rejected as before.</summary>
internal static class LenientJson
{
    private static readonly HashSet<string> Empty = new(StringComparer.OrdinalIgnoreCase) { "", "null", "none", "n/a", "undefined" };

    public static bool IsEmpty(string? s) => s is null || Empty.Contains(s.Trim());
}
