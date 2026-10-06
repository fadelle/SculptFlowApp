namespace PlasticSurgery.Entities.Models;

/// <summary>One price VERSION for one billable event type in one card, optionally narrowed by destination country,
/// operator, provider and provider billing responsibility (null = any). Never edited (a DB trigger enforces it): a
/// price change closes this row (EffectiveTo) and adds a new one, so usage that was rated with it keeps its price.</summary>
public class BillingRate
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }
    public string EventType { get; set; } = string.Empty;
    /// <summary>ISO 3166-1 alpha-2 destination country, or null for any.</summary>
    public string? CountryCode { get; set; }
    public string? Operator { get; set; }
    public string? Provider { get; set; }
    /// <summary>One of <see cref="ProviderBillingResponsibility"/>, or null. Null rates price SculptFlow-funded usage
    /// (and give provider cost for reporting on any usage); a rate set to e.g. customer_direct is a SculptFlow usage
    /// fee for accounts whose customer pays the provider. A customer-paid account is never charged a null rate.</summary>
    public string? ProviderBilling { get; set; }
    /// <summary>What one unit is ("message", "segment", "minute", "token"...) — informational.</summary>
    public string Unit { get; set; } = "unit";
    /// <summary>What the upstream provider charges per unit (paid by whoever the usage's responsibility says).</summary>
    public decimal ProviderCost { get; set; }
    /// <summary>What SculptFlow charges the clinic per unit. Independent of ProviderCost.</summary>
    public decimal ClientRate { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
