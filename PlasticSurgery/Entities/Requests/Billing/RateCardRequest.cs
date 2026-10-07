namespace PlasticSurgery.Entities.Requests.Billing;

public record RateCardRequest(string Code, string Name, string? Description, Guid? ClinicId, bool IsDefault = false);
