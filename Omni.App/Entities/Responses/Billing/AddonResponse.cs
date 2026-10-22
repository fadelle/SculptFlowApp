namespace Omni.App.Entities.Responses.Billing;

/// <summary>An add-on in the catalog, with how many clients have it.</summary>
public record AddonResponse(string Code, string Name, string EntitlementKey, decimal UnitPrice, string Currency, bool IsActive, int SortOrder, int Clients);
