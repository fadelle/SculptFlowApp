namespace PlasticSurgery.Entities.Models;

/// <summary>One entitlement of a plan. Keys are defined in code (Common/Statics/EntitlementKeys.cs). Value is "true"/"false"
/// for a feature and a number or "unlimited" for a limit. A key the plan doesn't list is off / 0.</summary>
public class SubscriptionPlanEntitlement
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string EntitlementKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
