using System.Globalization;
using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;

namespace PlasticSurgery.Common.Statics;

public static class EntitlementCatalog
{
    public const string Unlimited = "unlimited";

    public static readonly IReadOnlyList<EntitlementDefinition> All = new[]
    {
        new EntitlementDefinition(EntitlementKeys.Campaigns, EntitlementKind.Feature, "Campaigns"),
        new EntitlementDefinition(EntitlementKeys.AiAgent, EntitlementKind.Feature, "AI agent"),
        new EntitlementDefinition(EntitlementKeys.ApiAccess, EntitlementKind.Feature, "API access"),
        new EntitlementDefinition(EntitlementKeys.AdvancedReporting, EntitlementKind.Feature, "Advanced reporting"),
        new EntitlementDefinition(EntitlementKeys.MaxAgents, EntitlementKind.Limit, "Staff seats"),
        new EntitlementDefinition(EntitlementKeys.MaxWhatsAppNumbers, EntitlementKind.Limit, "WhatsApp numbers"),
        new EntitlementDefinition(EntitlementKeys.MaxChannelConnections, EntitlementKind.Limit, "Connected channels"),
    };

    public static EntitlementDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>Normalizes and validates a value for a key: features take true/false, limits a whole number or
    /// "unlimited". Throws ArgumentException otherwise.</summary>
    public static string NormalizeValue(string key, string? value)
    {
        var definition = Find(key) ?? throw new ArgumentException($"Unknown entitlement '{key}'.");
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (definition.Kind == EntitlementKind.Feature)
        {
            return v is "true" or "false" ? v : throw new ArgumentException($"'{key}' is a feature: use true or false.");
        }
        if (v == Unlimited) return v;
        return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 && v.Length <= 9
            ? n.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentException($"'{key}' is a limit: use a whole number or 'unlimited'.");
    }
}
