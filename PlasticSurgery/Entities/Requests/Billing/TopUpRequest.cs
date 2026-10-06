namespace PlasticSurgery.Entities.Requests.Billing;

public record TopUpRequest(decimal Amount, string? Reference, string? Reason);
