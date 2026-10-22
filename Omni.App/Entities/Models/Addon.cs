namespace Omni.App.Entities.Models;

/// <summary>A paid add-on in the catalog (billing.addons): extra units of one limit entitlement (users, channels, AI bots,
/// WhatsApp numbers), priced per unit per period.</summary>
public class Addon
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;

    /// <summary>The limit it raises (EntitlementKeys); one add-on per limit.</summary>
    public string EntitlementKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
