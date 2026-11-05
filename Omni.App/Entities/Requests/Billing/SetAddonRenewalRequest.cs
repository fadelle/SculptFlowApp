namespace Omni.App.Entities.Requests.Billing;

/// <summary>Whether a client's add-on renews with the plan: true, false, or null = follow Billing:AddonsRenewByDefault.</summary>
public record SetAddonRenewalRequest(bool? AutoRenew);
