namespace Omni.App.Entities.Dtos.Billing;

/// <summary>One add-on a client has: what it raises, how many units and what they cost per period.</summary>
public record ClientAddonRow(string Code, string Name, string EntitlementKey, int Quantity, decimal UnitPrice, string Currency, decimal PeriodPrice);
