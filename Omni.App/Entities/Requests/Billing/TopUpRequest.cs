namespace Omni.App.Entities.Requests.Billing;

public record TopUpRequest(decimal Amount, string? Reference, string? Reason);
