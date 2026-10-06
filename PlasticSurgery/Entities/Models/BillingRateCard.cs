namespace PlasticSurgery.Entities.Models;

/// <summary>A named price list. ClinicId set = that clinic's custom pricing; IsDefault = the fallback card.
/// Lookup order for a billable event: clinic card, then the plan's card, then the default card.</summary>
public class BillingRateCard
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ClinicId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
