namespace Omni.App.Entities.Models;

/// <summary>How many units of an add-on a client has (billing.client_addons). Its limit = the plan's value + Quantity.</summary>
public class ClientAddon
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid AddonId { get; set; }
    public int Quantity { get; set; }
    /// <summary>Renews with the plan: true / false as chosen, null = Billing:AddonsRenewByDefault.</summary>
    public bool? AutoRenew { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Addon? Addon { get; set; }
}
