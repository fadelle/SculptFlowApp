namespace Omni.App.Entities.Requests.Billing;

public record AdjustmentRequest(decimal Amount, string BalanceType, string Reason);
