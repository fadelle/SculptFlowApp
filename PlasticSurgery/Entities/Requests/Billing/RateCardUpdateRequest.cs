namespace PlasticSurgery.Entities.Requests.Billing;

public record RateCardUpdateRequest(string Name, string? Description, bool IsActive, bool IsDefault);
